using FoodDeliveryService.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FoodDeliveryService.Identity.Users;

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("api/users", RegisterUser)
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
}
