namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;

/// <summary>
/// Sends transactional emails. For local dev the implementation logs the message (and the activation
/// link) to the console/Seq; a real SMTP/SendGrid sender replaces it later. Treated as an external
/// call and instrumented accordingly.
/// </summary>
public interface IEmailService
{
    Task SendInvitationEmailAsync(
        string email,
        string firstName,
        string activationToken,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken = default);
}
