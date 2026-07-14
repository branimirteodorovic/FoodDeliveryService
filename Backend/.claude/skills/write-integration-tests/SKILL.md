---
description: Write full-stack integration tests for a FoodDeliveryService module (xUnit v3 + Testcontainers + real Duende auth + MassTransit/RabbitMQ). Use when testing an endpoint end-to-end, authorization, or cross-service event propagation.
disable-model-invocation: true
argument-hint: [ModuleName] [Feature]
---

# Write Integration Tests: $ARGUMENTS

Arguments: `$ARGUMENTS` — format: `{ModuleName} {Feature}` (e.g. `Restaurants OnboardRestaurant`).

Integration tests drive the **real HTTP endpoint through the module's full pipeline** (auth → MediatR → EF Core/Dapper → outbox), against ephemeral Postgres/Redis/RabbitMQ **Testcontainers**, with **real Duende JWTs**. They can also assert **cross-service propagation** by hosting another module's API in-process and polling its replica. Prefer these for: endpoint happy-path + status codes, authorization, and integration-event flow. Pure business-rule permutations belong in unit tests (`/write-unit-tests`).

Reference: `src/Modules/Restaurants/FoodDeliveryService.Modules.Restaurants.IntegrationTests/` — study `Abstractions/` (factory, base, collection, poller) and `Restaurants/*Tests.cs`.

## Prerequisite: local Identity must be running
Tests seed a **real** ASP.NET Identity credential against the Duende service at `http://localhost:18080`, which is **docker-compose, not a testcontainer** — it must already be up (`docker-compose up -d fooddeliveryservice.identity`). If it's down, every test fails at seeding/token acquisition, not with a useful assertion.

## Project (create once per module)

Path: `src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.IntegrationTests/`

### `.csproj`
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
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Testcontainers.RabbitMq" />
    <PackageReference Include="Testcontainers.Redis" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <!-- The system-under-test host -->
    <ProjectReference Include="..\..\..\API\FoodDeliveryService.{ModuleName}.Api\FoodDeliveryService.{ModuleName}.Api.csproj" />
    <!-- Users.Api answers the real permission RPC. Alias avoids the duplicate `Program` clash. -->
    <ProjectReference Include="..\..\..\API\FoodDeliveryService.Users.Api\FoodDeliveryService.Users.Api.csproj">
      <Aliases>UsersApi</Aliases>
    </ProjectReference>
    <!-- Add another host ONLY if you assert cross-service propagation into it (e.g. a replica) -->
    <ProjectReference Include="..\..\..\API\FoodDeliveryService.Orders.Api\FoodDeliveryService.Orders.Api.csproj">
      <Aliases>OrdersApi</Aliases>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```
Packages are version-managed centrally (no `Version` attribute). Add the project to `FoodDeliveryService.Api.slnx`.

## The four Abstractions (copy from Restaurants, rename namespace)

Copy these verbatim, changing only `{ModuleName}`, the SUT database name, and which extra API hosts you spin up:

1. **`IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime`** — the fixture. It:
   - Starts one Postgres (named `fooddeliveryservice_{modulename}`), one Redis, one RabbitMQ testcontainer.
   - Passes container connection strings **only via `Environment.SetEnvironmentVariable(...)`** in `ConfigureWebHost`. This is non-negotiable: `Program.cs` reads `ConnectionStrings:*` eagerly in top-level statements *before* `WebApplicationFactory`'s `ConfigureAppConfiguration` would apply — env vars are the only override visible in time. Also set `MessageProcessor:Outbox|Inbox:IntervalInSeconds = "1"` to speed up polling, and point `Authentication:MetadataAddress` at `http://localhost:18080/.well-known/openid-configuration` (the docker-internal hostname in appsettings won't resolve from `dotnet test`).
   - In `InitializeAsync`: start containers → build `UsersApiTestFactory` → `SeedTestUserAsync()` → build any other host (e.g. `OrdersApiTestFactory`) and **touch `.Services`** so it builds eagerly (migrations applied, MassTransit endpoints bound) before any event is published. Ordering matters — hosts share the same env-var keys, so build them strictly one after another, never interleaved.
   - `SeedTestUserAsync()`: register a **uniquely-emailed** (`+{Guid:N}`) real Identity user via client-credentials (`users:register` scope) → insert a matching `User.Create(..., Role.Administrator)` row directly into the Users host's DB. Administrator covers all permissions, so one seeded user serves every test.

2. **`UsersApiTestFactory(redisConn, rabbitMqConn) : WebApplicationFactory<UsersApi::Program>`** — in-process Users host so the permission RPC (`GetUserPermissionsRequest`) is answered for real. Owns its own Postgres container; **reuses** the shared Redis/RabbitMQ so the RPC round-trips over the same broker. If manager provisioning runs the real `ProvisionManagerUserRequest` RPC, also set `Duende:AdminUrl`/`Duende:TokenUrl` to `localhost:18080` so its Identity call resolves.

3. **`OrdersApiTestFactory(...)` (optional)** — same shape; include **only** when a test asserts propagation into that module (e.g. a replica materialized via inbox). Reuses shared Redis/RabbitMQ, owns its own Postgres.

4. **`BaseIntegrationTest : IDisposable`** decorated `[Collection(nameof(IntegrationTestCollection))]` — per-test base. Creates a DI scope + `HttpClient` from the factory, exposes `Faker`, and `GetAuthenticatedHttpClientAsync()` which fetches (once, cached, lock-guarded) a **password-grant** JWT for the seeded user via the public client and attaches it as a Bearer header.

Plus:
- **`IntegrationTestCollection`** — `[CollectionDefinition(nameof(IntegrationTestCollection))] public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestWebAppFactory>;`. One fixture shared across all test classes (containers start once).
- **`Poller`** — `Poller.WaitAsync<T>(timeout, async () => Result<T>)`: retries every 1s until success or timeout. Use it for anything asynchronous (event propagation, inbox processing). Returning `Failure` (a `Result<T>` from a null replica converts to `Failure(Error.NullValue)`) keeps it retrying.
- **`GlobalSuppressions.cs`** — suppresses `CA1515` for the two `public` fixtures xUnit requires.

## Test Class

Path: `.../{Feature}s/{Feature}Tests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.{ModuleName}.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.{ModuleName}.Presentation.{Feature}s; // the endpoint's Request type

namespace FoodDeliveryService.Modules.{ModuleName}.IntegrationTests.{Feature}s;

public class {Feature}Tests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Should_ReturnOk_WhenRequestValid()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        var request = new {Feature}.Request { /* fill from Faker */ };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "{module-route}", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Guid id = await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        id.Should().NotBeEmpty();
    }
}
```

### Cross-service propagation (poll another host's DI)
```csharp
Result<Restaurant> replica = await Poller.WaitAsync<Restaurant>(
    TimeSpan.FromSeconds(60),
    async () =>
    {
        await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRestaurantReplicaRepository>();
        return await repo.GetAsync(id, TestContext.Current.CancellationToken); // Restaurant? → Result<Restaurant>
    });

replica.IsSuccess.Should().BeTrue("the replica should be consumed by the Orders module");
replica.Value.Name.Should().Be(request.Name);
```
Assert propagation against the consuming host's **own repository via its DI** — do not add a test-only read endpoint to the other service.

## Rules
- **One fixture per collection**: every test class extends `BaseIntegrationTest` and is covered by `[Collection(nameof(IntegrationTestCollection))]`. Containers start once for the whole run.
- **Real auth**: drive endpoints through `GetAuthenticatedHttpClientAsync()`, not by resolving `ISender` and bypassing the pipeline. The seeded Administrator user has every permission.
- **Env vars only** for injecting container connection strings — see the `ConfigureWebHost` reason above. Never `ConfigureAppConfiguration` for connection strings.
- **`TestContext.Current.CancellationToken`** on every awaited HTTP/DB call (xUnit v3).
- **Poll, never `Task.Delay`**, for anything eventually-consistent (outbox ≤ interval, RabbitMQ, inbox ≤ interval). Give propagation a generous timeout (60s) — CI containers are slow.
- Deserialize responses into the **Presentation response DTO** (e.g. `RestaurantResponse`) or the primitive the endpoint returns — never a domain entity.
- Add a second host + its `*ApiTestFactory` **only** when a test needs it; each extra host = another Postgres container = slower runs.
- Assert HTTP status codes explicitly (`HttpStatusCode.OK`, `.NotFound`, `.Forbidden`) as well as body content.
- Build: `dotnet build src/Modules/{ModuleName}/FoodDeliveryService.Modules.{ModuleName}.IntegrationTests`. Run with Identity up: `dotnet test <that project>`.
