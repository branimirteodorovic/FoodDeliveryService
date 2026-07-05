namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Options for building invitation activation links (bound from the "InvitationEmail" section).
/// <see cref="BaseUrl"/> is the public gateway origin the invitee reaches; the activation path is
/// appended to it.
/// </summary>
internal sealed class InvitationEmailOptions
{
    public const string SectionName = "InvitationEmail";

    public string BaseUrl { get; init; } = "http://localhost:3000";
}
