using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Agents;

/// <summary>
/// Local read-only replica of a support agent (or an administrator, who can do everything an agent
/// can), keyed by the Users service's UserId and populated from UserRegistered/UserProfileUpdated
/// integration events. Two jobs, both of which would otherwise need a cross-service call that hard
/// rule #5 forbids: rendering "assigned to Jane Doe" on a ticket list, and answering whether an
/// assignment target actually exists before a ticket is handed to them.
/// <para>
/// As a projection of state another service owns it raises no domain events — Users already
/// published the originating ones — and it carries no business rules beyond the two setters below.
/// </para>
/// </summary>
public sealed class SupportAgentReplica : Entity
{
    private SupportAgentReplica()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    /// <summary>
    /// Whether the agent may still be assigned work. Users publishes no deactivation event today,
    /// so nothing clears this yet; the column exists so that when one arrives, retiring an agent is
    /// a projection change rather than a schema change on a populated table.
    /// </summary>
    public bool IsActive { get; private set; }

    public static SupportAgentReplica Create(Guid userId, string email, string firstName, string lastName)
    {
        return new SupportAgentReplica
        {
            Id = userId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };
    }

    public void Update(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }
}
