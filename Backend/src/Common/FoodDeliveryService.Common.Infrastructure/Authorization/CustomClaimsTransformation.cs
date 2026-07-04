using System.Security.Claims;
using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.Exceptions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.Infrastructure.Authorization;

/// <summary>
/// Runs after JWT validation on every authenticated request and enriches the principal with the
/// user's module-side id and permission claims. The Duende-issued token only proves identity;
/// permissions live in the Users service, so <see cref="IPermissionService"/> resolves them —
/// inside the Users service directly from its database, in every other service via a MassTransit
/// request/response call to Users, cached in Redis for 5 minutes. The permission claims added
/// here are what PermissionAuthorizationHandler checks for .RequireAuthorization("permission").
/// </summary>
internal sealed class CustomClaimsTransformation(IServiceScopeFactory serviceScopeFactory) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == CustomClaims.Sub))
        {
            return principal;
        }

        using IServiceScope scope = serviceScopeFactory.CreateScope();

        IPermissionService permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        string identityId = principal.GetIdentityId();

        Result<PermissionsResponse> result = await permissionService.GetUserPermissionsAsync(identityId);

        if (result.IsFailure)
        {
            throw new Application.Exceptions.ApplicationException(nameof(IPermissionService.GetUserPermissionsAsync), result.Error);
        }

        var claimsIdentity = new ClaimsIdentity();

        claimsIdentity.AddClaim(new Claim(CustomClaims.Sub, result.Value.UserId.ToString()));

        foreach (string permission in result.Value.Permissions)
        {
            claimsIdentity.AddClaim(new Claim(CustomClaims.Permission, permission));
        }

        principal.AddIdentity(claimsIdentity);

        return principal;
    }
}
