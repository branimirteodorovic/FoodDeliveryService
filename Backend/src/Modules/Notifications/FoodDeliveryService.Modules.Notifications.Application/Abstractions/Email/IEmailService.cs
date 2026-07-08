namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;

/// <summary>
/// Sends transactional emails. For local dev the implementation logs the message (and the activation
/// link) to the console/Seq; a real SMTP/SendGrid sender replaces it later. Treated as an external
/// call and instrumented accordingly.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// The generic send used by the Email notification channel. Every other send (including the
    /// invitation email) is expressed on top of this so there is a single code path.
    /// </summary>
    Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);

    Task SendInvitationEmailAsync(
        string email,
        string firstName,
        string activationToken,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken = default);
}
