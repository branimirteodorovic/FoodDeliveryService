using System.Text.Json;
using Dapper;
using Npgsql;

namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// The one deliberate exception to "never touch the database".
/// <para>
/// Managers and drivers are admin-provisioned by invitation: Identity generates a one-time
/// activation token and the only place it exists programmatically is the `UserInvitedDomainEvent`
/// payload in the Users outbox — everywhere else it is an email. The integration suites solve this
/// exact problem the same way (`Delivery.IntegrationTests.BaseIntegrationTest.GetActivationTokenAsync`),
/// and reimplementing Postgres access inside a k6 script to avoid one SELECT would be the worse
/// trade by a distance.
/// </para>
/// <para>
/// Note this reads the row, it never writes one: the account is still activated through the real
/// `users/accept-invitation` endpoint, so the seeded driver is indistinguishable from one who
/// clicked the link in their email.
/// </para>
/// </summary>
internal sealed class ActivationTokenReader(SeederOptions options)
{
    // `content` is jsonb, so both the projection and the filter cast to text — LIKE has no jsonb
    // overload, and asking for the column raw comes back as a type Dapper will not hand out as a
    // string.
    private const string Sql =
        """
        SELECT content::text
        FROM outbox_messages
        WHERE type = 'UserInvitedDomainEvent'
          AND content::text LIKE @EmailPattern
        ORDER BY occurred_on_utc DESC
        LIMIT 20
        """;

    /// <summary>
    /// The activation token for <paramref name="email"/>, or null if no invitation was ever raised
    /// for it.
    /// <para>
    /// The row is written in the same transaction as the provisioning, so it is there the moment
    /// the onboarding call returns — this is not waiting on the outbox job, and a brief retry only
    /// absorbs commit timing.
    /// </para>
    /// </summary>
    public async Task<string?> TryGetAsync(string email, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            string? token = await ReadAsync(email, cancellationToken);

            if (token is not null)
            {
                return token;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Fails with the connection string in the message. A wrong host here is the seeder's most
    /// likely misconfiguration — the tool runs on the host while every service's own connection
    /// string names the compose DNS entry.
    /// </summary>
    public async Task EnsureReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(options.UsersConnectionString);

            await connection.OpenAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new SeederException(
                $"cannot reach the Users database ({Redact(options.UsersConnectionString)}): {exception.Message}. " +
                "It is needed only to read invited drivers' activation tokens. Pass --users-connection " +
                "if Postgres is not on localhost:5432.");
        }
    }

    private async Task<string?> ReadAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(options.UsersConnectionString);

        await connection.OpenAsync(cancellationToken);

        IEnumerable<string> contents;

        try
        {
            contents = await connection.QueryAsync<string>(
                new CommandDefinition(
                    Sql,
                    new { EmailPattern = $"%\"{email}\"%" },
                    cancellationToken: cancellationToken));
        }
        catch (NpgsqlException exception)
        {
            throw new SeederException(
                $"reading the Users outbox for '{email}' failed: {exception.Message}");
        }

        foreach (string content in contents)
        {
            using var document = JsonDocument.Parse(content);

            if (document.RootElement.TryGetProperty("Email", out JsonElement invitedEmail) &&
                string.Equals(invitedEmail.GetString(), email, StringComparison.OrdinalIgnoreCase) &&
                document.RootElement.TryGetProperty("ActivationToken", out JsonElement activationToken))
            {
                return activationToken.GetString();
            }
        }

        return null;
    }

    private static string Redact(string connectionString) =>
        string.Join(
            ';',
            connectionString
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.TrimStart().StartsWith("Password", StringComparison.OrdinalIgnoreCase)));
}
