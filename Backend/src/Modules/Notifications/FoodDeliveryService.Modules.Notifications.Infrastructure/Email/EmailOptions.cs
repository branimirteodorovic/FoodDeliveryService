namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Options for the email sender (bound from the "Email" section). Only the <c>Log</c> provider is
/// implemented now; the shape lets an SMTP/SendGrid provider drop in later without touching callers.
/// The invitation activation link keeps its own <see cref="InvitationEmailOptions"/>.
/// </summary>
internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    // "Log" (dev, default) writes the message to Seq; "Smtp" is reserved for a real sender later.
    public string Provider { get; init; } = "Log";

    public string FromAddress { get; init; } = "no-reply@fooddeliveryservice.local";

    public string FromName { get; init; } = "Food Delivery Service";
}
