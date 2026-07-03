using System.Text.Json.Serialization;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

internal sealed record RegisterUserResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; }
}
