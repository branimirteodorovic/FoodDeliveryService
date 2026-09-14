using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.AttachWebhookPaymentMethod;

/// <summary>
/// The browser confirmed a SetupIntent against Stripe and Stripe told us about it. The card is
/// already attached at the provider by the time this runs — this is the platform catching up.
/// <para>
/// <b>It still calls the provider</b>, and that is not a formality: the display fields (brand, last
/// four, expiry) are on the payment method object, not on the SetupIntent event, and
/// <c>AttachPaymentMethodAsync</c> is idempotent for a card already on the customer (§6.5) — so the
/// one call both reads the card back and reconciles the attachment if the confirmation happened
/// without one.
/// </para>
/// <para>
/// <b>Idempotent end to end.</b> A redelivered event, or a second outbox dispatch of the same
/// domain event, reaches <see cref="CustomerPaymentProfile.Attach"/> with a <c>pm_…</c> the profile
/// already holds, which is a no-op that raises nothing — so no consumer re-projects a change that
/// did not happen.
/// </para>
/// </summary>
internal sealed class AttachWebhookPaymentMethodCommandHandler(
    IStripeEventLogRepository eventLogRepository,
    ICustomerPaymentProfileRepository profileRepository,
    IPaymentGateway paymentGateway,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<AttachWebhookPaymentMethodCommand>
{
    public async Task<Result> Handle(
        AttachWebhookPaymentMethodCommand request,
        CancellationToken cancellationToken)
    {
        StripeEventLog? eventLog = await eventLogRepository.GetAsync(request.EventLogId, cancellationToken);

        if (eventLog is null)
        {
            return Result.Failure(StripeEventLogErrors.NotFound(request.EventLogId));
        }

        if (eventLog.IsProcessed)
        {
            // A second dispatch of the same domain event. The attach below would be harmless, but it
            // is a provider round trip, and an event the platform has already finished with does not
            // get to spend one.
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(eventLog.PaymentMethodReference))
        {
            return Result.Failure(
                StripeEventLogErrors.EventIncomplete(eventLog.EventType, "payment method"));
        }

        if (string.IsNullOrWhiteSpace(eventLog.CustomerReference))
        {
            return Result.Failure(StripeEventLogErrors.EventIncomplete(eventLog.EventType, "customer"));
        }

        // The provider's customer id is the only link back to a platform customer here: a SetupIntent
        // confirmed in a browser carries no token of ours. The lookup is by the cus_… this service
        // itself created at registration (§6.2), on a unique index.
        CustomerPaymentProfile? profile = await profileRepository.GetByStripeCustomerIdAsync(
            eventLog.CustomerReference,
            cancellationToken);

        if (profile is null)
        {
            return Result.Failure(
                StripeEventLogErrors.CustomerProfileNotFound(eventLog.CustomerReference));
        }

        Result<GatewayPaymentMethod> attachResult = await paymentGateway.AttachPaymentMethodAsync(
            profile.StripeCustomerId,
            eventLog.PaymentMethodReference,
            PaymentIdempotencyKeys.AttachPaymentMethod(eventLog.PaymentMethodReference),
            cancellationToken);

        if (attachResult.IsFailure)
        {
            return Result.Failure(attachResult.Error);
        }

        GatewayPaymentMethod paymentMethod = attachResult.Value;

        Result attached = profile.Attach(
            Guid.CreateVersion7(),
            paymentMethod.PaymentMethodId,
            paymentMethod.Brand,
            paymentMethod.Last4,
            paymentMethod.ExpiryMonth,
            paymentMethod.ExpiryYear,
            dateTimeProvider.UtcNow);

        if (attached.IsFailure)
        {
            return Result.Failure(attached.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
