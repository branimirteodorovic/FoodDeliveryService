using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.RecordWebhookEvent;

/// <summary>
/// Verify, insert, return — and nothing else (§7.6).
/// <para>
/// <b>Everything this handler does is bounded by one database round trip.</b> Stripe times out at
/// 20 seconds and treats a slow endpoint as a failed one, so a handler that called Stripe back, or
/// that did the aggregate work inline, would turn a busy minute into a redelivery storm. The work is
/// the outbox's, driven by the domain event <see cref="StripeEventLog.Record"/> raises.
/// </para>
/// <para>
/// <b>Dedupe is two layers, the same shape as <c>PlaceOrderCommandHandler</c>'s idempotency key.</b>
/// The read catches the ordinary redelivery cheaply; the unique index catches the two deliveries
/// that are in flight at the same instant, which the read cannot see. Both answer success, because
/// "already handled" is exactly what a <c>200</c> means to Stripe — an error here would earn a
/// redelivery of an event that has already been acted on.
/// </para>
/// </summary>
internal sealed class RecordWebhookEventCommandHandler(
    IPaymentWebhookParser webhookParser,
    IStripeEventLogRepository eventLogRepository,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<RecordWebhookEventCommandHandler> logger) : ICommandHandler<RecordWebhookEventCommand>
{
    public async Task<Result> Handle(RecordWebhookEventCommand request, CancellationToken cancellationToken)
    {
        // The signature is the only authentication this endpoint has. Nothing below this line may
        // run on an unverified payload — not a log line quoting it, not a row recording it.
        Result<PaymentWebhookEvent> parseResult = webhookParser.Parse(request.Payload, request.Signature);

        if (parseResult.IsFailure)
        {
            // Warning rather than error: an anonymous endpoint on the public internet collects
            // unverifiable requests as a matter of course, and paging somebody for each one is how a
            // real alert gets muted. A genuine delivery always verifies, so a *sustained* rate here
            // means the secret is wrong — which is what the count, not the severity, will show.
            logger.LogWarning("A webhook was rejected: its signature did not verify");

            return Result.Failure(StripeEventLogErrors.SignatureInvalid);
        }

        PaymentWebhookEvent webhookEvent = parseResult.Value;

        if (await eventLogRepository.ExistsAsync(webhookEvent.EventId, cancellationToken))
        {
            logger.LogInformation(
                "Webhook {ProviderEventId} ({EventType}) was already recorded; acknowledging the redelivery",
                webhookEvent.EventId,
                webhookEvent.EventType);

            return Result.Success();
        }

        Result<StripeEventLog> logResult = StripeEventLog.Record(
            Guid.CreateVersion7(),
            webhookEvent.EventId,
            webhookEvent.EventType,
            webhookEvent.ObjectId,
            webhookEvent.ObjectStatus,
            webhookEvent.CustomerReference,
            webhookEvent.PaymentMethodReference,
            webhookEvent.OrderReference,
            webhookEvent.FailureReason,
            dateTimeProvider.UtcNow);

        if (logResult.IsFailure)
        {
            return Result.Failure(logResult.Error);
        }

        eventLogRepository.Insert(logResult.Value);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // The second layer. Two deliveries of one event racing: the loser hits the unique index
            // on provider_event_id. If the winner's row is now visible this is a duplicate and the
            // answer is success — anything else was a real failure, so rethrow.
            if (await eventLogRepository.ExistsAsync(webhookEvent.EventId, cancellationToken))
            {
                // The exception is carried even though this is an expected outcome: it is the
                // evidence that the unique index did the deduplicating, and without it a genuine
                // failure that happens to coincide with a redelivery reads as a routine duplicate.
                logger.LogInformation(
                    exception,
                    "Webhook {ProviderEventId} lost the race to record itself; acknowledging the duplicate",
                    webhookEvent.EventId);

                return Result.Success();
            }

            throw;
        }

        return Result.Success();
    }
}
