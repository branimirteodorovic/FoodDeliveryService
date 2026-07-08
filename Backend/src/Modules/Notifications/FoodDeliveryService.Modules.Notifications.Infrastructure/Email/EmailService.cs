using System.Diagnostics;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Dev email sender: logs the message (subject + body + recipient) to console/Seq instead of sending
/// real mail. Swap for an SMTP/SendGrid implementation later — <see cref="EmailOptions.Provider"/> is
/// the seam. The send is wrapped in an OpenTelemetry activity so it shows up as a span once a real
/// external call is added. <see cref="SendInvitationEmailAsync"/> is expressed on top of the generic
/// <see cref="SendEmailAsync"/> so there is one code path.
/// </summary>
internal sealed class EmailService(
    IOptions<InvitationEmailOptions> invitationOptions,
    ILogger<EmailService> logger) : IEmailService
{
    internal static readonly ActivitySource ActivitySource = new("FoodDeliveryService.Notifications.Email");

    private readonly InvitationEmailOptions _invitationOptions = invitationOptions.Value;

    public Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        using Activity? activity = ActivitySource.StartActivity("SendEmail");
        activity?.SetTag("email.to", toEmail);
        activity?.SetTag("email.subject", subject);

        // Dev-only: the full message is logged rather than emailed.
        logger.LogInformation(
            "Email to {ToEmail} — {Subject}\n{Body}",
            toEmail,
            subject,
            htmlBody);

        return Task.CompletedTask;
    }

    public Task SendInvitationEmailAsync(
        string email,
        string firstName,
        string activationToken,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken = default)
    {
        string activationLink =
            $"{_invitationOptions.BaseUrl.TrimEnd('/')}/users/accept-invitation" +
            $"?email={Uri.EscapeDataString(email)}" +
            $"&token={Uri.EscapeDataString(activationToken)}";

        const string subject = "Activate your Food Delivery Service account";

        string body =
            $"Hi {firstName},\n\n" +
            $"Your account has been created. Activate it before {expiresOnUtc:u} using the link below:\n" +
            $"{activationLink}";

        return SendEmailAsync(email, subject, body, cancellationToken);
    }
}
