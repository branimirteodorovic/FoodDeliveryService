using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.GetRefundRequests;

/// <summary>
/// The refund queue: an administrator's list of decisions to make (<c>?status=Requested</c>), and an
/// agent's view of what they and their colleagues have asked for.
/// <para>
/// Staff-only, so unlike the ticket list there is no ownership narrowing here — every caller who
/// reaches this holds <c>refunds:request</c>, which no customer ever does. A customer learns about
/// their own refund from the decision email and from their ticket thread.
/// </para>
/// </summary>
public sealed record GetRefundRequestsQuery(
    string? Status,
    int Page,
    int PageSize) : IQuery<IReadOnlyCollection<RefundRequestResponse>>;
