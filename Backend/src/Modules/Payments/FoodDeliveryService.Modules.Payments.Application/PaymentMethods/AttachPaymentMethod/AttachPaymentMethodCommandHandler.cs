using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.AttachPaymentMethod;

/// <summary>
/// Attaches at the provider first, then records locally. That order is the one that fails safely: a
/// crash between the two leaves a card attached at Stripe that this platform does not know about,
/// which costs nothing and is cleaned up by the next attach. The reverse order leaves a row claiming
/// a card that does not exist, and the first order placed against it fails at authorization.
/// </summary>
internal sealed class AttachPaymentMethodCommandHandler(
    ICustomerPaymentProfileRepository profileRepository,
    IPaymentGateway paymentGateway,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AttachPaymentMethodCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AttachPaymentMethodCommand request, CancellationToken cancellationToken)
    {
        CustomerPaymentProfile? profile = await profileRepository.GetAsync(request.CustomerId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<Guid>(CustomerPaymentProfileErrors.NotFound(request.CustomerId));
        }

        Result<GatewayPaymentMethod> attachResult = await paymentGateway.AttachPaymentMethodAsync(
            profile.StripeCustomerId,
            request.StripePaymentMethodId,
            PaymentIdempotencyKeys.AttachPaymentMethod(request.StripePaymentMethodId),
            cancellationToken);

        if (attachResult.IsFailure)
        {
            return Result.Failure<Guid>(attachResult.Error);
        }

        GatewayPaymentMethod paymentMethod = attachResult.Value;

        // A v7 GUID so the public identifier sorts by when the card was saved — the same choice the
        // rest of the platform makes for ids that end up in an index.
        var paymentMethodId = Guid.CreateVersion7();

        Result attached = profile.Attach(
            paymentMethodId,
            paymentMethod.PaymentMethodId,
            paymentMethod.Brand,
            paymentMethod.Last4,
            paymentMethod.ExpiryMonth,
            paymentMethod.ExpiryYear,
            dateTimeProvider.UtcNow);

        if (attached.IsFailure)
        {
            return Result.Failure<Guid>(attached.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Re-reading the property rather than returning the local: attaching the same pm_… twice is
        // a no-op in the aggregate (§6.1), and in that case the caller must be told the id of the
        // card that is actually saved, not the one this attempt minted and discarded.
        return profile.PaymentMethodId!.Value;
    }
}
