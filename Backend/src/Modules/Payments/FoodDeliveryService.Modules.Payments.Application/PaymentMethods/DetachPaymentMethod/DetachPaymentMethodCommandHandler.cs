using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.DetachPaymentMethod;

/// <summary>
/// Detaches at the provider, then clears the local row — the order §6.3 specifies, and the one that
/// cannot leave this platform charging a card the customer believes they removed.
/// <para>
/// The ownership check is the lookup itself: the profile is read by the caller's own id, so a card
/// id belonging to somebody else simply is not on the row that comes back and the aggregate answers
/// "not found". There is no branch in which one customer's request can reach another's card.
/// </para>
/// </summary>
internal sealed class DetachPaymentMethodCommandHandler(
    IPaymentsContext paymentsContext,
    ICustomerPaymentProfileRepository profileRepository,
    IPaymentGateway paymentGateway,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DetachPaymentMethodCommand>
{
    public async Task<Result> Handle(DetachPaymentMethodCommand request, CancellationToken cancellationToken)
    {
        Guid customerId = paymentsContext.UserId;

        CustomerPaymentProfile? profile = await profileRepository.GetAsync(customerId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure(CustomerPaymentProfileErrors.NotFound(customerId));
        }

        if (profile.PaymentMethodId != request.PaymentMethodId || profile.StripePaymentMethodId is null)
        {
            // Checked before the provider call so a wrong id does not reach Stripe at all, and
            // checked again by the aggregate below — the second one is the invariant, this one is
            // what keeps the network call off the failing path.
            return Result.Failure(CustomerPaymentProfileErrors.PaymentMethodNotFound(request.PaymentMethodId));
        }

        Result detached = await paymentGateway.DetachPaymentMethodAsync(
            profile.StripePaymentMethodId,
            PaymentIdempotencyKeys.DetachPaymentMethod(profile.StripePaymentMethodId),
            cancellationToken);

        if (detached.IsFailure)
        {
            return detached;
        }

        Result cleared = profile.Detach(request.PaymentMethodId, dateTimeProvider.UtcNow);

        if (cleared.IsFailure)
        {
            return cleared;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
