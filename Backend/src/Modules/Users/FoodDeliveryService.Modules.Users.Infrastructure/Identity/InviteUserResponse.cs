using System.Text.Json.Serialization;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

internal sealed record InviteUserResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("activationToken")]
    public string ActivationToken { get; init; }

    [JsonPropertyName("expiresOnUtc")]
    public DateTime ExpiresOnUtc { get; init; }
}
