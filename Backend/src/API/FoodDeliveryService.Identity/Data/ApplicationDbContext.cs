using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Identity.Data;

/// <summary>
/// The ASP.NET Core Identity store (users, roles, claims, logins, tokens) — and, since Feature 3.7
/// Milestone E, the ASP.NET Data Protection key ring as well.
/// <para>
/// The key ring belongs in the database for the same reason the signing keys do: its default home is
/// a directory under the content root, which in a container is per-pod and disappears on restart.
/// Two things depend on it and both break when it is replica-local. Duende encrypts the signing keys
/// it persists to <c>PersistedGrantDbContext</c> with this ring, so a shared key store protected by
/// an unshared ring is no better than no store at all; and the one-time invitation activation tokens
/// (<c>GeneratePasswordResetToken</c>, three-day lifespan) are data-protection payloads, so a
/// restart or a second replica turns a valid activation link into an invalid one.
/// </para>
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
