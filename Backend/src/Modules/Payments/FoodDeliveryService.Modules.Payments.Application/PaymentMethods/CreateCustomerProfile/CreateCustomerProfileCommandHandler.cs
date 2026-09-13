using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateCustomerProfile;

internal sealed class CreateCustomerProfileCommandHandler(
    ICustomerPaymentProfileRepository profileRepository,
    IPaymentGateway paymentGateway,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateCustomerProfileCommand>
{
    public async Task<Result> Handle(CreateCustomerProfileCommand request, CancellationToken cancellationToken)
    {
        // The inbox dispatches at least once, so a redelivered registration lands here again. The
        // early return is what keeps that from creating a second Stripe customer for one person —
        // the idempotency key below would also catch it inside the 24-hour window, but the window is
        // the provider's promise, not ours.
        CustomerPaymentProfile? existing = await profileRepository.GetAsync(request.UserId, cancellationToken);

        if (existing is not null)
        {
            return Result.Success();
        }

        Result<GatewayCustomer> customerResult = await paymentGateway.CreateCustomerAsync(
            request.UserId,
            request.Email,
            $"{request.FirstName} {request.LastName}",
            PaymentIdempotencyKeys.Customer(request.UserId),
            cancellationToken);

        if (customerResult.IsFailure)
        {
            // The event handler above turns this into an ApplicationException, which the inbox job
            // records on the message row. It does NOT retry (§5.6), so a customer whose creation
            // failed has no profile until something re-drives it — today that is a second
            // registration event, and from §7 it is the reconciling webhook path. Returning success
            // here would hide it entirely, which is the worse of the two.
            return Result.Failure(customerResult.Error);
        }

        profileRepository.Insert(CustomerPaymentProfile.Create(
            request.UserId,
            customerResult.Value.StripeCustomerId,
            dateTimeProvider.UtcNow));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
