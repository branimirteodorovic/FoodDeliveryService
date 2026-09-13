using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.DetachPaymentMethod;

/// <summary>
/// Removes the caller's saved card — §6.3. The customer is the authenticated caller and is not a
/// field here: the only thing the route names is which card, and the aggregate refuses an id that is
/// not the one currently saved.
/// </summary>
public sealed record DetachPaymentMethodCommand(Guid PaymentMethodId) : ICommand;
