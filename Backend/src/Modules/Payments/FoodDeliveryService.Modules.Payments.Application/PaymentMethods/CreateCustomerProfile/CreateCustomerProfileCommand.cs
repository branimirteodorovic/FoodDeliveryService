using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateCustomerProfile;

/// <summary>
/// Creates the Stripe customer for a newly registered user and the local profile row that records
/// it — §6.2.
/// <para>
/// Driven only by <c>UserRegisteredIntegrationEvent</c>, never by an endpoint, which is why it
/// carries no validator: its fields come from an event that was validated at its source, and a
/// rejection here would be a dropped replica rather than a 400 to anybody
/// (<c>ValidatorCoverageTests</c> scopes itself to endpoint-reachable requests for exactly this
/// reason).
/// </para>
/// </summary>
public sealed record CreateCustomerProfileCommand(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName) : ICommand;
