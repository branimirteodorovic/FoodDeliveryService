using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Application.Users.GetCurrentUser;

/// <summary>
/// Reads the caller's row and their roles in one round trip. The subject is
/// <see cref="IUsersContext.UserId"/> and there is no parameter that could name anyone else.
/// </summary>
internal sealed class GetCurrentUserQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IUsersContext usersContext)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // LEFT JOIN, not JOIN: a user with no role rows is a broken account rather than a missing
        // one, and an inner join would report it as a 404 the client would read as "signed in as
        // nobody". They get their profile and an empty role list, which is what it is.
        //
        // The roles come back one per row and are grouped below rather than aggregated in SQL —
        // array_agg would need an Npgsql type handler for the string[] on the way back, for a set
        // that is at most a handful of rows wide.
        const string sql =
            $"""
             SELECT
                 u.id AS {nameof(CurrentUserRow.UserId)},
                 u.email AS {nameof(CurrentUserRow.Email)},
                 u.first_name AS {nameof(CurrentUserRow.FirstName)},
                 u.last_name AS {nameof(CurrentUserRow.LastName)},
                 ur.role_name AS {nameof(CurrentUserRow.Role)}
             FROM users u
             LEFT JOIN user_roles ur ON ur.user_id = u.id
             WHERE u.id = @UserId
             ORDER BY ur.role_name
             """;

        List<CurrentUserRow> rows = (await connection.QueryAsync<CurrentUserRow>(
            sql,
            new { usersContext.UserId })).AsList();

        // Unreachable on a well-formed request: CustomClaimsTransformation resolved this very id out
        // of this very table to mint the claim. It stays a 404 rather than an exception for the one
        // case that is not well-formed — the account deactivated between the token being minted and
        // this read — because "your account is gone" is a client-side fact, not a server failure.
        if (rows.Count == 0)
        {
            return Result.Failure<CurrentUserResponse>(UserErrors.NotFound(usersContext.UserId));
        }

        CurrentUserRow user = rows[0];

        return new CurrentUserResponse(
            user.UserId,
            user.Email,
            user.FirstName,
            user.LastName,
            rows.Select(row => row.Role).OfType<string>().ToList());
    }

    internal sealed class CurrentUserRow
    {
        internal Guid UserId { get; init; }

        internal string Email { get; init; }

        internal string FirstName { get; init; }

        internal string LastName { get; init; }

        /// <summary>Null on the single row a user with no roles produces — see the LEFT JOIN.</summary>
        internal string? Role { get; init; }
    }
}
