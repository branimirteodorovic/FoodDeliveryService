using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RequestRefund;

/// <summary>
/// An agent asks for a customer to be refunded on the order their ticket is about.
/// <para>
/// Note what is not here: the order, the customer and the requesting agent. The first two come from
/// the ticket and the third from the token. An agent who could name the order would be able to
/// refund an order their case never mentioned, and one who could name the requester would be able
/// to put somebody else's id on a request they later approved themselves.
/// </para>
/// </summary>
public sealed record RequestRefundCommand(Guid TicketId, decimal Amount, string Reason) : ICommand<Guid>;
