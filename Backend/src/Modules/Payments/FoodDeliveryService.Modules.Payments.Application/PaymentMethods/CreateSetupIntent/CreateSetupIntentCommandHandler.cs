using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateSetupIntent;

/// <summary>
/// Creates the SetupIntent and returns its client secret. Writes nothing: the SetupIntent is not the
/// attachment (§1.3) — the browser confirms it against Stripe, and the <c>setup_intent.succeeded</c>
/// webhook is what records the card (§7). A handler that saved the card here would be recording one
/// the customer has not entered yet.
/// </summary>
internal sealed class CreateSetupIntentCommandHandler(
    IPaymentsContext paymentsContext,
    ICustomerPaymentProfileRepository profileRepository,
    IPaymentGateway paymentGateway)
    : ICommandHandler<CreateSetupIntentCommand, SetupIntentResponse>
{
    public async Task<Result<SetupIntentResponse>> Handle(
        CreateSetupIntentCommand request,
        CancellationToken cancellationToken)
    {
        Guid customerId = paymentsContext.UserId;

        CustomerPaymentProfile? profile = await profileRepository.GetAsync(customerId, cancellationToken);

        if (profile is null)
        {
            // Registered a moment ago and the UserRegistered event has not been consumed yet, or its
            // Stripe customer creation failed. Either way there is nothing to attach a card to, and
            // creating the customer here would put a provider call on a path §6.2 deliberately took
            // it off.
            return Result.Failure<SetupIntentResponse>(CustomerPaymentProfileErrors.NotFound(customerId));
        }

        Result<GatewaySetupIntent> setupIntentResult = await paymentGateway.CreateSetupIntentAsync(
            profile.StripeCustomerId,
            PaymentIdempotencyKeys.SetupIntent(Guid.CreateVersion7()),
            cancellationToken);

        if (setupIntentResult.IsFailure)
        {
            return Result.Failure<SetupIntentResponse>(setupIntentResult.Error);
        }

        return new SetupIntentResponse(
            setupIntentResult.Value.SetupIntentId,
            setupIntentResult.Value.ClientSecret);
    }
}
