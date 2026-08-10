namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Turns an invited account (restaurant manager, driver) into one that can log in — exactly the way
/// the invitee would: read the one-time token, post it to `users/accept-invitation`, choose a
/// password.
/// </summary>
internal sealed class InvitedAccountActivator(
    PlatformClient client,
    ActivationTokenReader tokenReader,
    SeederOptions options)
{
    /// <summary>
    /// Activates <paramref name="email"/> unless it is already usable, and returns its access token.
    /// <para>
    /// The "already usable" check is a login attempt, which is what makes the seeder resumable:
    /// a run interrupted between onboarding and activation leaves an account that cannot log in but
    /// cannot be onboarded again either, and the outbox row holding its token is still there.
    /// </para>
    /// </summary>
    public async Task<string> ActivateAsync(string email, CancellationToken cancellationToken)
    {
        string? existing = await client.TryGetTokenAsync(email, options.SeededPassword, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        string? activationToken = await tokenReader.TryGetAsync(email, cancellationToken);

        if (activationToken is null)
        {
            throw new SeederException(
                $"no UserInvitedDomainEvent for '{email}' in the Users outbox, so it was never invited — " +
                "if this account exists with a different password, re-seed a clean stack " +
                "(`docker compose down -v`) or pass --seeded-password.");
        }

        ApiResult<Empty> accepted = await client.TryAcceptInvitationAsync(
            email,
            activationToken,
            options.SeededPassword,
            cancellationToken);

        if (!accepted.IsSuccess)
        {
            throw new SeederException(
                $"activating '{email}' failed: {accepted.Detail}. A one-time token that was already " +
                "used cannot be replayed — the account exists with a password this run does not know.");
        }

        return await client.GetTokenAsync(email, options.SeededPassword, cancellationToken);
    }
}
