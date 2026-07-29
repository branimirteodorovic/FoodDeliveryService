using System.Net;
using AwesomeAssertions;
using FoodDeliveryService.Common.Infrastructure.Caching;
using StackExchange.Redis;

namespace FoodDeliveryService.Common.UnitTests.Caching;

/// <summary>
/// The hardening contract of the shared Redis connection: what the connection string is allowed to
/// decide (everything about the endpoint and the timeouts) versus what it is not (aborting on a
/// connect failure, and running an Azure Cache endpoint without TLS).
/// </summary>
public class RedisConnectionOptionsTests
{
    private const string LocalConnectionString = "fooddeliveryservice.redis:6379";
    private const string AzureCacheHost = "fooddeliveryservice.redis.cache.windows.net";

    [Fact]
    public void Create_Should_DisableAbortOnConnectFail()
    {
        // Act
        ConfigurationOptions options = RedisConnectionOptions.Create(LocalConnectionString);

        // Assert
        options.AbortOnConnectFail.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_DisableAbortOnConnectFail_EvenWhenTheConnectionStringAsksForIt()
    {
        // Arrange — AddInfrastructure depends on Connect() returning a reconnecting multiplexer
        // instead of throwing, so this one option is forced rather than defaulted.
        const string connectionString = $"{LocalConnectionString},abortConnect=true";

        // Act
        ConfigurationOptions options = RedisConnectionOptions.Create(connectionString);

        // Assert
        options.AbortOnConnectFail.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_BackOffExponentiallyBetweenReconnectAttempts()
    {
        // Act
        ConfigurationOptions options = RedisConnectionOptions.Create(LocalConnectionString);

        // Assert — the StackExchange.Redis default is linear, which has every replica that lost the
        // same node retrying in lockstep.
        options.ReconnectRetryPolicy.Should().BeOfType<ExponentialRetry>();
    }

    [Fact]
    public void Create_Should_IdentifyTheHostToRedis_WhenAClientNameIsGiven()
    {
        // Act
        ConfigurationOptions options = RedisConnectionOptions.Create(
            LocalConnectionString,
            "FoodDeliveryService.Restaurants.Api");

        // Assert
        options.ClientName.Should().Be("FoodDeliveryService.Restaurants.Api");
    }

    [Theory]
    [InlineData($"{AzureCacheHost}:6380")]
    [InlineData("fooddeliveryservice.eastus.redis.azure.net:10000")]
    public void Create_Should_YieldTls_ForAnAzureCacheEndpointThatOmitsIt(string connectionString)
    {
        // Act — an Azure Cache endpoint serves nothing unencrypted, so a connection string without
        // ssl=True is a mistake, not a choice. StackExchange.Redis recognises both DNS suffixes
        // (classic and Managed Redis) and turns TLS on itself; this pins that we inherit it rather
        // than having to add it, so a future change to Create can't quietly drop it.
        ConfigurationOptions options = RedisConnectionOptions.Create(connectionString);

        // Assert
        options.Ssl.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_LeaveTlsOff_ForALocalEndpoint()
    {
        // Act — the local container and the Testcontainers instance are plaintext.
        ConfigurationOptions options = RedisConnectionOptions.Create(LocalConnectionString);

        // Assert
        options.Ssl.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_KeepEverythingTheAzureConnectionStringSpecifies()
    {
        // Arrange — the shape the Azure portal hands out (credential omitted).
        const string connectionString = $"{AzureCacheHost}:6380,ssl=True,abortConnect=False,connectTimeout=15000";

        // Act
        ConfigurationOptions options = RedisConnectionOptions.Create(connectionString);

        // Assert
        options.Ssl.Should().BeTrue();
        options.ConnectTimeout.Should().Be(15000);
        options.EndPoints.Should().ContainSingle()
            .Which.Should().BeOfType<DnsEndPoint>()
            .Which.Should().Match<DnsEndPoint>(endPoint => endPoint.Host == AzureCacheHost && endPoint.Port == 6380);
    }

    [Fact]
    public void Create_Should_KeepTuningFromTheConnectionString()
    {
        // Arrange — timeouts and retry counts stay per-environment knobs; hardening does not
        // overwrite what an operator deliberately set.
        const string connectionString = $"{LocalConnectionString},connectTimeout=1234,connectRetry=7,syncTimeout=4321";

        // Act
        ConfigurationOptions options = RedisConnectionOptions.Create(connectionString);

        // Assert
        options.ConnectTimeout.Should().Be(1234);
        options.ConnectRetry.Should().Be(7);
        options.SyncTimeout.Should().Be(4321);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_WhenConnectionStringIsMissing(string connectionString)
    {
        // Act
        Action act = () => RedisConnectionOptions.Create(connectionString);

        // Assert — a host with no cache endpoint is misconfigured, not degraded.
        act.Should().Throw<ArgumentException>();
    }
}
