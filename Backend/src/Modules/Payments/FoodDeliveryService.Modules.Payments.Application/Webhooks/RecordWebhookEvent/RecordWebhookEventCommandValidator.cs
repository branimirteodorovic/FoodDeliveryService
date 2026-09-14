using FluentValidation;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.RecordWebhookEvent;

/// <summary>
/// The boundary check on an endpoint the whole internet can reach. It runs before any HMAC is
/// computed, so a body that is not even shaped like an event costs a string comparison rather than a
/// digest over however many megabytes the caller chose to send.
/// </summary>
internal sealed class RecordWebhookEventCommandValidator : AbstractValidator<RecordWebhookEventCommand>
{
    /// <summary>
    /// Comfortably above the events this module subscribes to — a <c>setup_intent.succeeded</c> is a
    /// couple of kilobytes — and far below what an anonymous caller would like to make this service
    /// hash. Stripe's own limit on an event payload is larger; if a subscribed event type ever
    /// approaches this, raise it deliberately rather than discovering it as a 400 in production.
    /// </summary>
    private const int MaximumPayloadLength = 8_192;

    /// <summary>
    /// A <c>Stripe-Signature</c> header is <c>t=…,v1=…</c> and is around a hundred characters. The
    /// bound is on the same argument as the payload's: it is hashed, so its length is work.
    /// </summary>
    private const int MaximumSignatureLength = 512;

    public RecordWebhookEventCommandValidator()
    {
        RuleFor(c => c.Payload)
            .NotEmpty()
            .MaximumLength(MaximumPayloadLength)
            // A provider event is a JSON object. This rejects the obviously-not-an-event body
            // without reaching the verification, and it is the rule that makes an overlong run of
            // filler fail here rather than one character past the length bound.
            .Must(payload => payload is not null && payload.TrimStart().StartsWith('{'))
            .WithMessage("A webhook payload is a JSON object.");

        RuleFor(c => c.Signature)
            .NotEmpty()
            .MaximumLength(MaximumSignatureLength);
    }
}
