using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Customers.RegisterCustomerAccount;

/// <summary>
/// Records account creation from UserRegistered — the account age every "new account" signal in
/// Milestone E is measured against. Inbox-driven, hence upsert semantics.
/// <para>
/// It is applied for <b>every</b> registered user, not only ones carrying the Customer role. Roles
/// change after registration and the event is only a snapshot of the moment it was published, so
/// filtering on it here would silently leave a later customer with an unknown account age. A row
/// for a driver or a manager costs one Guid and never scores: the signals require orders.
/// </para>
/// </summary>
public sealed record RegisterCustomerAccountCommand(Guid CustomerId, DateTime RegisteredOnUtc) : ICommand;
