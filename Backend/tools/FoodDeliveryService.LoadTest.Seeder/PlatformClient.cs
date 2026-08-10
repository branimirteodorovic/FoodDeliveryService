using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// The platform as a load-test client sees it: the Gateway for everything, Duende for tokens, and
/// no other door. Hard Rule 10 says all external traffic goes through the Gateway, and a fixture
/// built any other way describes a system nobody runs — worse, writing rows straight into a
/// database would skip the outbox, so Orders would never receive the restaurant/menu replica and
/// every seeded order would fail with <c>RestaurantNotFound</c>.
/// </summary>
internal sealed class PlatformClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly SeederOptions _options;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    public PlatformClient(SeederOptions options)
    {
        _options = options;

        _http = new HttpClient
        {
            // Generous: a cold service's first authenticated request measured ~3 s during Milestone
            // A, and seeding is the very first traffic a fresh stack ever sees.
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public void Dispose() => _http.Dispose();

    // ── Authentication ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ROPC against Duende's public client, cached per account. Callers ask for a token freely; only
    /// the first call for an account pays for the PBKDF2 hash.
    /// </summary>
    public async Task<string> GetTokenAsync(string email, string password, CancellationToken cancellationToken)
    {
        string? token = await TryGetTokenAsync(email, password, cancellationToken);

        return token ?? throw new SeederException(
            $"could not authenticate '{email}' at {_options.IdentityUrl}. " +
            "Wrong password, or the account was never activated?");
    }

    /// <summary>
    /// The same, but a rejected credential is an answer rather than a failure — it is how every
    /// "does this account already exist?" check in the seeder is made, without a database read.
    /// </summary>
    public async Task<string?> TryGetTokenAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(email, out string? cached))
        {
            return cached;
        }

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", "fooddeliveryservice-public-client"),
            new KeyValuePair<string, string>("scope", "openid profile email fooddeliveryservice.api"),
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("username", email),
            new KeyValuePair<string, string>("password", password),
        ]);

        using HttpResponseMessage response = await _http.PostAsync(
            new Uri($"{_options.IdentityUrl}/connect/token"),
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken);

        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            return null;
        }

        _tokens[email] = token.AccessToken;

        return token.AccessToken;
    }

    // ── Restaurants ───────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RestaurantSummary>> GetAllRestaurantsAsync(
        string token,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100; // GetRestaurantsQueryValidator caps it here.

        var all = new List<RestaurantSummary>();
        int page = 1;

        while (true)
        {
            IReadOnlyList<RestaurantSummary>? batch = await GetAsync<IReadOnlyList<RestaurantSummary>>(
                $"restaurants?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={pageSize}",
                token,
                cancellationToken);

            if (batch is null || batch.Count == 0)
            {
                return all;
            }

            all.AddRange(batch);

            if (batch.Count < pageSize)
            {
                return all;
            }

            page++;
        }
    }

    public Task<MenuResponse?> GetMenuAsync(Guid restaurantId, string token, CancellationToken cancellationToken) =>
        GetAsync<MenuResponse>($"restaurants/{restaurantId}/menu", token, cancellationToken);

    public Task<Guid> OnboardRestaurantAsync(RestaurantSpec spec, string token, CancellationToken cancellationToken) =>
        PostAsync<Guid>(
            "restaurants",
            new
            {
                spec.Name,
                spec.TaxIdentification,
                spec.CuisineType,
                spec.Email,
                spec.PhoneNumber,
                spec.Street,
                spec.City,
                spec.PostalCode,
                spec.Country,
                spec.Latitude,
                spec.Longitude,
                spec.CommissionRate,
                spec.ManagerEmail,
                spec.ManagerFirstName,
                spec.ManagerLastName,
            },
            token,
            cancellationToken);

    public Task<Guid> CreateMenuCategoryAsync(
        Guid restaurantId,
        CategorySpec spec,
        string token,
        CancellationToken cancellationToken) =>
        PostAsync<Guid>(
            $"restaurants/{restaurantId}/menu-categories",
            new { spec.Name, spec.DisplayOrder },
            token,
            cancellationToken);

    public Task<Guid> CreateMenuItemAsync(
        Guid restaurantId,
        Guid categoryId,
        MenuItemSpec spec,
        string token,
        CancellationToken cancellationToken) =>
        PostAsync<Guid>(
            $"restaurants/{restaurantId}/menu-items",
            new
            {
                CategoryId = categoryId,
                spec.Name,
                spec.Description,
                spec.Price,
                PhotoUrl = (string?)null,
                IsAvailable = true,
            },
            token,
            cancellationToken);

    // ── Users ─────────────────────────────────────────────────────────────────────────────────

    public Task<ApiResult<Guid>> TryRegisterCustomerAsync(
        CustomerSpec spec,
        string password,
        CancellationToken cancellationToken) =>
        TrySendAsync<Guid>(
            HttpMethod.Post,
            "users/register",
            new { spec.Email, Password = password, spec.FirstName, spec.LastName },
            token: null,
            idempotencyKey: null,
            cancellationToken);

    public Task<ApiResult<Empty>> TryAcceptInvitationAsync(
        string email,
        string activationToken,
        string newPassword,
        CancellationToken cancellationToken) =>
        TrySendAsync<Empty>(
            HttpMethod.Post,
            "users/accept-invitation",
            new { Email = email, Token = activationToken, NewPassword = newPassword },
            token: null,
            idempotencyKey: null,
            cancellationToken);

    // ── Delivery ──────────────────────────────────────────────────────────────────────────────

    public Task<ApiResult<Guid>> TryOnboardDriverAsync(
        DriverSpec spec,
        string token,
        CancellationToken cancellationToken) =>
        TrySendAsync<Guid>(
            HttpMethod.Post,
            "delivery/drivers",
            new { spec.Email, spec.FirstName, spec.LastName, spec.VehicleType },
            token,
            idempotencyKey: null,
            cancellationToken);

    /// <summary>
    /// The driver id of the authenticated caller. Onboarding returns it too; this is how a re-run
    /// recovers it for a driver that was already onboarded by an earlier run.
    /// </summary>
    public async Task<Guid> GetMyDriverIdAsync(string token, CancellationToken cancellationToken)
    {
        DriverProfile? profile = await GetAsync<DriverProfile>("delivery/drivers/me", token, cancellationToken);

        return profile?.Id ?? throw new SeederException("GET delivery/drivers/me returned no driver.");
    }

    public async Task SetAvailabilityAsync(bool available, string token, CancellationToken cancellationToken)
    {
        ApiResult<Empty> result = await TrySendAsync<Empty>(
            HttpMethod.Patch,
            "delivery/drivers/me/availability",
            new { Available = available },
            token,
            idempotencyKey: null,
            cancellationToken);

        // Already available is not a failure worth stopping a re-run for — the aggregate refuses the
        // no-op transition and the driver is in exactly the state we wanted.
        if (!result.IsSuccess && result.Status != (int)HttpStatusCode.BadRequest)
        {
            throw new SeederException($"PATCH delivery/drivers/me/availability failed: {result.Detail}");
        }
    }

    public async Task RecordLocationAsync(
        double latitude,
        double longitude,
        string token,
        CancellationToken cancellationToken)
    {
        ApiResult<Empty> result = await TrySendAsync<Empty>(
            HttpMethod.Post,
            "delivery/drivers/me/location",
            new { Latitude = latitude, Longitude = longitude },
            token,
            idempotencyKey: null,
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new SeederException($"POST delivery/drivers/me/location failed: {result.Detail}");
        }
    }

    // ── Orders ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The probe. Its failure is the interesting case, so it returns rather than throws: until both
    /// replicas have arrived in the Orders database this is a 400 naming the missing one.
    /// </summary>
    public Task<ApiResult<Guid>> TryPlaceOrderAsync(
        FixtureRestaurant restaurant,
        IReadOnlyList<Guid> menuItemIds,
        string token,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        TrySendAsync<Guid>(
            HttpMethod.Post,
            "orders",
            new
            {
                restaurant.RestaurantId,
                Items = menuItemIds.Select(id => new { MenuItemId = id, Quantity = 1 }).ToArray(),
                DeliveryAddress = new
                {
                    Street = "Load Test Street 1",
                    restaurant.City,
                    restaurant.PostalCode,
                    restaurant.Country,
                    Notes = "seeder replica probe",
                    restaurant.Latitude,
                    restaurant.Longitude,
                },
                PaymentMethod = "CashOnDelivery",
            },
            token,
            idempotencyKey,
            cancellationToken);

    // ── Plumbing ──────────────────────────────────────────────────────────────────────────────

    private async Task<T?> GetAsync<T>(string path, string token, CancellationToken cancellationToken)
    {
        ApiResult<T> result = await TrySendAsync<T>(
            HttpMethod.Get,
            path,
            body: null,
            token,
            idempotencyKey: null,
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new SeederException($"GET {path} failed: {result.Detail}");
        }

        return result.Value;
    }

    private async Task<T> PostAsync<T>(string path, object body, string token, CancellationToken cancellationToken)
    {
        ApiResult<T> result = await TrySendAsync<T>(
            HttpMethod.Post,
            path,
            body,
            token,
            idempotencyKey: null,
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new SeederException($"POST {path} failed: {result.Detail}");
        }

        return result.Value!;
    }

    private async Task<ApiResult<T>> TrySendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? token,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri($"{_options.GatewayUrl}/{path}"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        // Same correlation scheme the k6 scripts use (lib/http.js), so a seeding run is findable in
        // Seq by prefix too — which is how you tell "the seeder never called it" apart from "the
        // call was made and the handler failed".
        request.Headers.Add("X-Correlation-Id", $"seeder-{_options.RunId}");

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);

        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return ApiResult<T>.Failure((int)response.StatusCode, $"{(int)response.StatusCode} {Truncate(content)}");
        }

        if (typeof(T) == typeof(Empty) || content.Length == 0)
        {
            return ApiResult<T>.Success(default);
        }

        try
        {
            return ApiResult<T>.Success(JsonSerializer.Deserialize<T>(content, Json));
        }
        catch (JsonException exception)
        {
            return ApiResult<T>.Failure((int)response.StatusCode, $"unreadable body ({exception.Message})");
        }
    }

    private static string Truncate(string content) =>
        content.Length > 400 ? $"{content[..400]}…" : content;

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }

    private sealed record DriverProfile(Guid Id);
}

/// <summary>The result of a call whose failure the caller wants to inspect rather than catch.</summary>
internal readonly record struct ApiResult<T>(bool IsSuccess, T? Value, int Status, string Detail)
{
    public static ApiResult<T> Success(T? value) => new(true, value, 200, string.Empty);

    public static ApiResult<T> Failure(int status, string detail) => new(false, default, status, detail);
}

/// <summary>Marker for the endpoints that answer `204 No Content`.</summary>
internal sealed record Empty;

internal sealed record RestaurantSummary(Guid Id, string Name, string TaxIdentification);

internal sealed record MenuResponse(Guid RestaurantId, IReadOnlyList<MenuCategoryResponse> Categories);

internal sealed record MenuCategoryResponse(Guid Id, string Name, IReadOnlyList<MenuItemResponse> Items);

internal sealed record MenuItemResponse(Guid Id, string Name, decimal Price, bool IsAvailable);
