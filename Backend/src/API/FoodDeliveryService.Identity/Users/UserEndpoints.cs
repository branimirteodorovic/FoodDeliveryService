using FoodDeliveryService.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Identity.Users;

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder builder)
    {
        // Customer self-registration path (real password) — called by the Users module during
        // users/register.
        builder.MapPost("api/users", RegisterUser)
            .RequireAuthorization(Config.UsersRegisterPolicy);

        // Admin-provisioned staff path: create an invited account with NO password and mint a
        // one-time activation token.
        builder.MapPost("api/users/invite", InviteUser)
            .RequireAuthorization(Config.UsersRegisterPolicy);

        // Activation path: consume the one-time token and set the invitee's chosen password.
        builder.MapPost("api/users/set-password", SetPassword)
            .RequireAuthorization(Config.UsersRegisterPolicy);

        // Compensation path: remove a provisioned account whose onboarding failed downstream.
        // Only never-activated invited accounts may be deleted.
        builder.MapDelete("api/users/{id}", DeleteInvitedUser)
            .RequireAuthorization(Config.UsersRegisterPolicy);

        return builder;
    }

    private static async Task<IResult> RegisterUser(
        [FromBody] RegisterUserRequest request,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            EmailConfirmed = true
        };

        IdentityResult result = await userManager.CreateAsync(user, request.Password);

        if (result.Succeeded)
        {
            return Results.Created($"/api/users/{user.Id}", new RegisterUserResponse(user.Id));
        }

        bool isDuplicate = result.Errors.Any(error =>
            error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            return Results.Conflict();
        }

        var errors = result.Errors.ToDictionary(
            error => error.Code,
            error => new[] { error.Description });

        return Results.ValidationProblem(errors);
    }

    private static async Task<IResult> InviteUser(
        [FromBody] InviteUserRequest request,
        UserManager<ApplicationUser> userManager,
        IOptions<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions> tokenProviderOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Invited accounts start with NO password and a "must change password" flag — they cannot
        // log in until the invitee activates the account by setting a password (see SetPassword).
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            EmailConfirmed = true,
            MustChangePassword = true
        };

        IdentityResult result = await userManager.CreateAsync(user);

        if (!result.Succeeded)
        {
            bool isDuplicateUser = result.Errors.Any(error =>
                error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

            if (isDuplicateUser)
            {
                return Results.Conflict();
            }

            var createErrors = result.Errors.ToDictionary(
                error => error.Code,
                error => new[] { error.Description });

            return Results.ValidationProblem(createErrors);
        }

        // One-time activation token (same mechanism as a password reset). Its lifespan is the
        // configured data-protection token lifespan; expose the expiry so the invitation email can
        // communicate it and expired links can be rejected.
        string activationToken = await userManager.GeneratePasswordResetTokenAsync(user);

        DateTime expiresOnUtc = DateTime.UtcNow.Add(tokenProviderOptions.Value.TokenLifespan);

        return Results.Created(
            $"/api/users/{user.Id}",
            new InviteUserResponse(user.Id, activationToken, expiresOnUtc));
    }

    private static async Task<IResult> DeleteInvitedUser(
        string id,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(id);

        // Idempotent: an already-removed account means the compensation goal is reached.
        if (user is null)
        {
            return Results.NotFound();
        }

        // Safety guard: compensation must never destroy an account the invitee already activated.
        if (!user.MustChangePassword)
        {
            return Results.Conflict();
        }

        IdentityResult result = await userManager.DeleteAsync(user);

        if (!result.Succeeded)
        {
            var errors = result.Errors.ToDictionary(
                error => error.Code,
                error => new[] { error.Description });

            return Results.ValidationProblem(errors);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> SetPassword(
        [FromBody] SetPasswordRequest request,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByEmailAsync(request.Email);

        // Do not reveal whether the account exists; a bad email is treated the same as a bad token.
        if (user is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Token"] = ["The activation link is invalid or has expired."]
            });
        }

        IdentityResult result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = result.Errors.ToDictionary(
                error => error.Code,
                error => new[] { error.Description });

            return Results.ValidationProblem(errors);
        }

        // Account is now activated — clear the invited flag so it behaves like any other account.
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        return Results.NoContent();
    }
}
