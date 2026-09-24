using FluentValidation;

namespace FoodDeliveryService.Modules.Users.Application.Users.ChangeUserRoles;

internal sealed class ChangeUserRolesCommandValidator : AbstractValidator<ChangeUserRolesCommand>
{
    /// <summary>The <c>roles.name</c> column's width — a name longer than this matches no row.</summary>
    private const int RoleNameMaxLength = 50;

    /// <summary>
    /// More than the platform has assignable roles, so a legitimate request can never hit it, and
    /// small enough that a caller cannot hand the role join an unbounded IN list.
    /// </summary>
    private const int MaxRoles = 10;

    public ChangeUserRolesCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();

        // The empty set is refused in the aggregate, not here: "a user must hold at least one role"
        // is an invariant of User, and a validator that also owned it would let the two drift.
        // What is bounded here is only the shape a caller can send.
        RuleFor(c => c.Roles).NotNull();
        RuleFor(c => c.Roles).Must(roles => roles.Count <= MaxRoles)
            .When(c => c.Roles is not null)
            .WithMessage($"At most {MaxRoles} roles may be assigned in one request.");

        RuleForEach(c => c.Roles)
            .NotEmpty()
            .MaximumLength(RoleNameMaxLength)
            .When(c => c.Roles is not null);
    }
}
