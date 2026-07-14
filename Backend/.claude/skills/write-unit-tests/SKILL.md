---
description: Write domain unit tests for a FoodDeliveryService module (xUnit v3 + AwesomeAssertions + Bogus). Use when adding unit tests for an aggregate's business logic, factory methods, invariants, and domain events.
disable-model-invocation: true
argument-hint: [ModuleName] [Aggregate]
---

# Write Unit Tests: $ARGUMENTS

Arguments: `$ARGUMENTS` — format: `{ModuleName} {Aggregate}` (e.g. `Restaurants Restaurant` or `Orders Order`).

Unit tests cover **domain logic only** — the aggregate's static factory, business methods, invariants, and the domain events they raise. No database, no DI, no HTTP. If you need to exercise a command/query handler end-to-end, that's an integration test (`/write-integration-tests`).

Reference: `src/Modules/Restaurants/FoodDeliveryService.Modules.Restaurants.UnitTests/` (the only complete example — study `Restaurants/RestaurantsTests.cs` and `Abstractions/BaseTest.cs`).

## Project (create once per module, skip if it already exists)

Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.UnitTests/`

### `FoodDeliveryService.Modules.{ModuleName}.UnitTests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Bogus" />
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <!-- Domain only — unit tests never reference Application/Infrastructure/Presentation -->
    <ProjectReference Include="..\FoodDeliveryService.Modules.{ModuleName}.Domain\FoodDeliveryService.Modules.{ModuleName}.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```
Versions are centrally managed — reference packages **without** a `Version` attribute (`Directory.Packages.props` supplies it). Add the project to `FoodDeliveryService.Api.slnx`.

### `Abstractions/BaseTest.cs`
```csharp
using Bogus;
using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.{ModuleName}.UnitTests.Abstractions;

#pragma warning disable CA1515 // Consider making public types internal
public abstract class BaseTest
#pragma warning restore CA1515 // Consider making public types internal
{
    protected static readonly Faker Faker = new();

    public static T AssertDomainEventWasPublished<T>(Entity entity)
        where T : IDomainEvent
    {
        T? domainEvent = entity.DomainEvents.OfType<T>().SingleOrDefault();

        if (domainEvent is null)
        {
            throw new Exception($"{typeof(T).Name} was not published");
        }

        return domainEvent;
    }
}
```

## Test Class

Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.UnitTests/{Aggregate}s/{Aggregate}sTests.cs`

```csharp
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.{ModuleName}.Domain.{Aggregate}s;
using FoodDeliveryService.Modules.{ModuleName}.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.{ModuleName}.UnitTests.{Aggregate}s;

public class {Aggregate}sTests : BaseTest
{
    [Fact]
    public void Create_ShouldRaiseDomainEvent_When{Aggregate}IsCreated()
    {
        // Arrange & Act
        {Aggregate} aggregate = Create{Aggregate}();

        // Assert
        {Aggregate}RegisteredDomainEvent domainEvent =
            AssertDomainEventWasPublished<{Aggregate}RegisteredDomainEvent>(aggregate);
        domainEvent.{Aggregate}Id.Should().Be(aggregate.Id);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Create_ShouldReturnFailure_WhenXIsOutOfBounds(decimal x)
    {
        // Act
        Result<{Aggregate}> result = {Aggregate}.Create(/* ... */, x /* ... */);

        // Assert
        result.Error.Should().Be({Aggregate}Errors.InvalidX);
    }

    // A private factory keeps each test's Arrange short. Overloads with `out` params expose the
    // generated values when a test needs to re-supply them (e.g. "unchanged" no-op assertions).
    private static {Aggregate} Create{Aggregate}()
    {
        return {Aggregate}.Create(
            Guid.NewGuid(),
            Faker.Company.CompanyName(),
            /* ...other args, mostly from Faker... */).Value;
    }
}
```

## What to cover (one `[Fact]`/`[Theory]` per behavior)

For every public domain method on the aggregate:
- **Success path** — state mutated as expected (`.Should().Be(...)`), `result.IsSuccess.Should().BeTrue()`.
- **Each failure/invariant** — `result.IsFailure.Should().BeTrue()` and `result.Error.Should().Be({Aggregate}Errors.SomeError)`. Use the exact error from the `{Aggregate}Errors` class; for parameterized errors assert equality against the same call (e.g. `MenuCategoryErrors.NotFound(id)`).
- **Domain event raised** on success — `AssertDomainEventWasPublished<TEvent>(aggregate)`, then assert the event's payload fields.
- **Domain event NOT raised** when a "change" is a no-op — `aggregate.DomainEvents.OfType<TEvent>().Should().BeEmpty()`. This is the classic guard against emitting events when nothing actually changed.
- Bound checks via `[Theory] [InlineData(...)]` (e.g. out-of-range rates, invalid prices).
- Child-entity operations (add/update collection members) — assert the collection contents *and* the event.

## Rules
- Test classes are `public` and extend `BaseTest`; test methods follow `Method_ShouldExpected_WhenCondition`.
- Arrange / Act / Assert comment blocks, exactly as the reference.
- Construct aggregates **only** through their static factory / public methods — never `new {Aggregate}(...)` and never reflection. If setup needs a child entity, build it through the parent's real API (e.g. `restaurant.AddMenuCategory(...).Value`).
- Use `Bogus` `Faker` for incidental data; use literal constants only when the value is the thing under test.
- Assert on `Result`/`Result<T>` (`IsSuccess`, `IsFailure`, `Error`, `Value`) — the domain never throws for business failures, so don't `Assert.Throws` for those.
- Keep it pure: no `IServiceProvider`, no DbContext, no HTTP, no async (domain methods are synchronous).
- Build to verify: `dotnet build src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.UnitTests` then `dotnet test` the same project.
