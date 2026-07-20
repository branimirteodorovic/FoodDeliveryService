using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FoodDeliveryService.RealTime.Api.Extensions;

/// <summary>
/// Adds the SignalR access-token hook to JwtBearer. A browser WebSocket handshake cannot send an
/// <c>Authorization</c> header, so the SignalR JS client sends the JWT as the <c>access_token</c>
/// query-string parameter instead; this reads it back into <see cref="JwtBearerOptions"/> — but only
/// for <c>/hubs/*</c> paths, so ordinary requests keep using the header exclusively. All the other
/// JwtBearer settings (audience, valid issuers, metadata address) are bound from the
/// "Authentication" section by the shared JwtBearerConfigureOptions in AddInfrastructure; this only
/// layers the hook on top and leaves that binding untouched.
/// </summary>
internal static class HubAuthenticationExtensions
{
    private const string HubPathPrefix = "/hubs";
    private const string AccessTokenQueryParameter = "access_token";

    internal static IServiceCollection AddRealTimeHubAuthentication(this IServiceCollection services)
    {
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    string? accessToken = context.Request.Query[AccessTokenQueryParameter];

                    PathString path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments(HubPathPrefix))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }
}
