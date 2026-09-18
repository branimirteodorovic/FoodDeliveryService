using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Application.Diagnostics;
using FoodDeliveryService.Modules.Payments.Application.Webhooks.AttachWebhookPaymentMethod;
using FoodDeliveryService.Modules.Payments.Application.Webhooks.ConfirmPaymentAuthorization;
using FoodDeliveryService.Modules.Payments.Application.Webhooks.ConfirmPaymentCapture;
using FoodDeliveryService.Modules.Payments.Application.Webhooks.FailPaymentAuthorization;
using FoodDeliveryService.Modules.Payments.Application.Webhooks.MarkWebhookEventProcessed;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.RecordWebhookEvent;

/// <summary>
/// The dispatch table for provider events — Feature 3.8 Milestone E.
/// <para>
/// It runs on <c>ProcessOutboxJob</c>, a second or so after the endpoint answered, which is the
/// whole point of §7.6: the provider's 20-second clock stopped at the insert, and the Stripe round
/// trips the work needs happen out here where they cost nothing.
/// </para>
/// <para>
/// <b>Every later milestone adds one arm below and one command beside it</b> —
/// <c>charge.refunded</c> in §10 (§8's two arrived with Milestone F and §9's third is below them).
/// Each arm must be idempotent
/// on its own, because arrival order carries no meaning (§7.5) and because this handler is
/// dispatched at least once.
/// </para>
/// <para>
/// <b>An unrecognised type is not a failure.</b> Stripe sends whatever the dashboard is subscribed
/// to, and a type nothing here acts on is a recorded event with no work attached — it is still
/// marked processed, so the log's outstanding rows mean "owed work" rather than "traffic".
/// </para>
/// </summary>
internal sealed class StripeEventReceivedDomainEventHandler(
    ISender sender,
    IDateTimeProvider dateTimeProvider,
    ILogger<StripeEventReceivedDomainEventHandler> logger)
    : DomainEventHandler<StripeEventReceivedDomainEvent>
{
    public override async Task Handle(
        StripeEventReceivedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Feature 3.8 Milestone I. Recorded FIRST, unlike every other instrument in this module, and
        // that is not an oversight: the measurement is the wait that is already over by the time this
        // line runs, so taking it at the end would fold the work into the queueing it exists to
        // isolate. It is also the one number that still needs recording when the dispatch below
        // fails — an event that could not be acted on waited exactly as long as one that could.
        PaymentsDiagnostics.RecordWebhookLag(
            TagFor(domainEvent.EventType),
            Math.Max(0, (dateTimeProvider.UtcNow - domainEvent.ReceivedOnUtc).TotalSeconds));

        Result result = domainEvent.EventType switch
        {
            PaymentWebhookEventTypes.SetupIntentSucceeded => await sender.Send(
                new AttachWebhookPaymentMethodCommand(domainEvent.EventLogId),
                cancellationToken),

            // Milestone F's two arms. Both mutate a Payment, so both take IDistributedLock before
            // their read — which the arm above deliberately does not, because the profile it writes
            // is already protected by a unique index (§7.7).
            PaymentWebhookEventTypes.PaymentIntentAmountCapturableUpdated => await sender.Send(
                new ConfirmPaymentAuthorizationCommand(domainEvent.EventLogId),
                cancellationToken),

            // Milestone G. Same shape, same lock, one step further down the lifecycle: the
            // provider saying the money moved, for the times the capture call's own response did
            // not come back.
            PaymentWebhookEventTypes.PaymentIntentSucceeded => await sender.Send(
                new ConfirmPaymentCaptureCommand(domainEvent.EventLogId),
                cancellationToken),

            PaymentWebhookEventTypes.PaymentIntentPaymentFailed => await sender.Send(
                new FailPaymentAuthorizationCommand(domainEvent.EventLogId),
                cancellationToken),

            _ => Result.Success()
        };

        if (result.IsFailure)
        {
            logger.LogError(
                "Webhook {ProviderEventId} ({EventType}) could not be acted on: {ErrorCode} {ErrorDescription}",
                domainEvent.ProviderEventId,
                domainEvent.EventType,
                result.Error.Code,
                result.Error.Description);

            // Recorded on the log row before the throw, because the throw is the end of the line:
            // ProcessOutboxJob writes the exception onto the outbox message and marks it processed —
            // it does NOT retry (§5.6). The unprocessed row with an error on it is what tells an
            // operator this event is still owed some work, and the reconciling path is a fresh
            // delivery of the same event from the Stripe dashboard.
            await sender.Send(
                new MarkWebhookEventProcessedCommand(domainEvent.EventLogId, result.Error.Description),
                cancellationToken);

            throw new Common.Application.Exceptions.ApplicationException(domainEvent.EventType, result.Error);
        }

        Result marked = await sender.Send(
            new MarkWebhookEventProcessedCommand(domainEvent.EventLogId, Error: null),
            cancellationToken);

        if (marked.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(MarkWebhookEventProcessedCommand),
                marked.Error);
        }
    }

    /// <summary>
    /// The event type, bounded to the four this module dispatches on. Everything else is
    /// <c>other</c>, because the tag is Stripe's own string and the set of types a dashboard can be
    /// subscribed to is a hundred-odd values this platform does not control — exactly the kind of
    /// open set that turns one time series into a hundred. Nothing is lost: an event type nobody
    /// acts on has no lag worth attributing, and the log row still names it.
    /// </summary>
    private static string TagFor(string eventType) => eventType switch
    {
        PaymentWebhookEventTypes.SetupIntentSucceeded
            or PaymentWebhookEventTypes.PaymentIntentAmountCapturableUpdated
            or PaymentWebhookEventTypes.PaymentIntentSucceeded
            or PaymentWebhookEventTypes.PaymentIntentPaymentFailed => eventType,
        _ => "other"
    };
}
