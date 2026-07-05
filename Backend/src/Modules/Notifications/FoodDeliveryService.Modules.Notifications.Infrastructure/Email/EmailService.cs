using System.Diagnostics;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Dev email sender: builds the activation link and logs the invitation (to console/Seq) instead of
/// sending real mail. Swap for an SMTP/SendGrid implementation later. The send is wrapped in an
/// OpenTelemetry activity so it shows up as a span once a real external call is added.
/// </summary>
internal sealed class EmailService(
    IOptions<InvitationEmailOptions> options,
    ILogger<EmailService> logger) : IEmailService
{
    internal static readonly ActivitySource ActivitySource = new("FoodDeliveryService.Notifications.Email");

    private readonly InvitationEmailOptions _options = options.Value;

    public Task SendInvitationEmailAsync(
        string email,
        string firstName,
        string activationToken,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken = default)
    {
        using Activity? activity = ActivitySource.StartActivity("SendInvitationEmail");
        activity?.SetTag("email.to", email);

        string activationLink =
            $"{_options.BaseUrl.TrimEnd('/')}/users/accept-invitation" +
            $"?email={Uri.EscapeDataString(email)}" +
            $"&token={Uri.EscapeDataString(activationToken)}";

        // Dev-only: the link (with the one-time token) is logged rather than emailed.
        logger.LogInformation(
            "Invitation email for {Email} ({FirstName}): activate before {ExpiresOnUtc:u} via {ActivationLink}",
            email,
            firstName,
            expiresOnUtc,
            activationLink);

        return Task.CompletedTask;
    }
}
