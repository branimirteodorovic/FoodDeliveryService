using Bogus;

namespace FoodDeliveryService.Modules.FraudDetection.UnitTests.Abstractions;

#pragma warning disable CA1515 // Consider making public types internal
public abstract class BaseTest
#pragma warning restore CA1515 // Consider making public types internal
{
    protected static readonly Faker Faker = new();

    // A fixed clock. Every assertion in this suite is about a window boundary or an ordering, so a
    // drifting DateTime.UtcNow would make the interesting cases flaky exactly at the boundary.
    protected static readonly DateTime Now = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    protected static readonly TimeSpan Window = TimeSpan.FromHours(24);
}
