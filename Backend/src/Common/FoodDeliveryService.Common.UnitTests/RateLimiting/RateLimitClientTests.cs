using System.Net;
using System.Security.Claims;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.UnitTests.RateLimiting;

/// <summary>
/// Partition selection — the half of a rate limiter that is usually wrong.
/// <para>
/// Both failure modes are silent. Counting an authenticated caller by IP throttles a whole office,
/// a carrier NAT or a VPN because of one member of it, and lets a single account escape its budget
/// by changing networks. Counting an anonymous caller by nothing at all means
/// <c>users/register</c> — unauthenticated by design, and the endpoint an abusive client reaches for
/// — has no limit.
/// </para>
/// </summary>
public class RateLimitClientTests
{
    private const string Subject = "0195c0f5-3b1e-7c2f-9a44-2f7c9a1c5e10";

    [Fact]
    public void Resolve_Should_PartitionAnAuthenticatedCallerBySubject()
    {
        // Arrange
        HttpContext context = Context(subject: Subject, address: "203.0.113.10");

        // Act
        string key = RateLimitClient.Resolve(context);

        // Assert — the account, not the address it happens to be arriving from.
        key.Should().Be($"{RateLimitClient.SubjectPrefix}:{Subject}");
    }

    [Fact]
    public void Resolve_Should_PartitionAnAuthenticatedCallerBySubject_WhenTheClaimIsMapped()
    {
        // Arrange — JwtBearer's inbound claim mapping rewrites `sub` to ClaimTypes.NameIdentifier
        // unless it is switched off. Both forms have to produce the same key, or turning that
        // setting on silently demotes every signed-in caller to an IP partition.
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Subject)],
            authenticationType: "Test");

        HttpContext context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        // Act
        string key = RateLimitClient.Resolve(context);

        // Assert
        key.Should().Be($"{RateLimitClient.SubjectPrefix}:{Subject}");
    }

    [Fact]
    public void Resolve_Should_PartitionAnAnonymousCallerByAddress()
    {
        // Arrange
        HttpContext context = Context(subject: null, address: "203.0.113.10");

        // Act
        string key = RateLimitClient.Resolve(context);

        // Assert
        key.Should().Be($"{RateLimitClient.AddressPrefix}:203.0.113.10");
    }

    [Fact]
    public void Resolve_Should_GiveTwoSubjectsTwoPartitions()
    {
        // Arrange — the same address, which is the case that matters: an office behind one NAT must
        // not be one bucket.
        HttpContext first = Context(subject: Subject, address: "203.0.113.10");
        HttpContext second = Context(subject: "0195c0f5-3b1e-7c2f-9a44-ffffffffffff", address: "203.0.113.10");

        // Act & Assert
        RateLimitClient.Resolve(first).Should().NotBe(RateLimitClient.Resolve(second));
    }

    [Fact]
    public void Resolve_Should_IgnoreClaimsOnAnUnauthenticatedPrincipal()
    {
        // Arrange — a ClaimsIdentity with no authentication type is not authenticated, and its
        // claims are whatever the request said they were. Trusting them would let any caller pick
        // its own partition key and never be limited twice.
        HttpContext context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Subject)])),
        };

        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        // Act
        string key = RateLimitClient.Resolve(context);

        // Assert
        key.Should().Be($"{RateLimitClient.AddressPrefix}:203.0.113.10");
    }

    [Fact]
    public void Resolve_Should_FallBackToOneSharedPartition_WhenNothingIdentifiesTheCaller()
    {
        // Arrange — no principal and no remote address. Rare, and deliberately the *strictest*
        // outcome rather than the most generous: unattributable traffic shares one bucket, because
        // a partition per unattributable request is the same as having no limiter.
        HttpContext context = new DefaultHttpContext();

        // Act
        string key = RateLimitClient.Resolve(context);

        // Assert
        key.Should().Be(RateLimitClient.UnattributedKey);
    }

    private static DefaultHttpContext Context(string? subject, string address)
    {
        var context = new DefaultHttpContext();

        if (subject is not null)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", subject)], authenticationType: "Test"));
        }

        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        return context;
    }
}
