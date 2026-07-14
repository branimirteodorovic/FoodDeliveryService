using System.Reflection;
using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Users.Infrastructure;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Serilog;
using FoodDeliveryService.Common.Application;
using FoodDeliveryService.Users.Api.Extensions;
using FoodDeliveryService.Users.Api.OpenTelemetry;
using FoodDeliveryService.Users.Api.Middleware;
using FoodDeliveryService.Users.Api.Seed;

// API host for the Users module (:5400) — reached through the YARP gateway via users/**.
// Owns registration, profiles, roles and permissions, and answers GetUserPermissionsRequest
// (MassTransit request/response) for the other services' authorization.

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging (Console + Seq sinks, configured in appsettings "Serilog").
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Last-resort exception handling: unhandled exceptions become RFC 7807 ProblemDetails responses.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenAPI document (/openapi in Development) + Swagger UI for exploring the module's endpoints.
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();

// MediatR + FluentValidation for the module's Application assembly (commands, queries, validators).
Assembly[] moduleApplicationAssemblies = [
    FoodDeliveryService.Modules.Users.Application.AssemblyReference.Assembly];

builder.Services.AddApplication(moduleApplicationAssemblies);

// Every service gets its OWN backing stores: a dedicated PostgreSQL database
// (fooddeliveryservice_users), the shared Redis cache and the RabbitMQ broker.
string databaseConnectionString = builder.Configuration.GetConnectionStringOrThrow("Database");
string redisConnectionString = builder.Configuration.GetConnectionStringOrThrow("Cache");
var rabbitMqSettings = new RabbitMqSettings(builder.Configuration.GetConnectionStringOrThrow("Queue"));

// Shared infrastructure stack (see InfrastructureConfiguration): JWT auth (Duende), permission
// authorization, Npgsql + Dapper, Quartz outbox/inbox jobs, Redis caching, MassTransit/RabbitMQ
// messaging (registering this module's consumers), and OpenTelemetry tracing to Jaeger.
builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [UsersModule.ConfigureConsumers()],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString);

Uri duendeHealthUrl = builder.Configuration.GetDuendeHealthUrl();

// AspNetCore.HealthChecks.* packages: liveness probes for every external dependency —
// PostgreSQL (Npgsql), Redis, RabbitMQ (reuses the raw IConnection registered in
// AddInfrastructure) and the Duende IdentityServer /health endpoint.
builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString)
    .AddRedis(redisConnectionString)
    .AddRabbitMQ(sp => sp.GetRequiredService<IConnection>())
    .AddDuende(duendeHealthUrl);

// Module-specific registrations: DbContext, repositories, domain/integration event handlers,
// endpoints, outbox/inbox job schedules (see UsersModule).
builder.Services.AddUsersModule(builder.Configuration);

WebApplication app = builder.Build();

// EF Core migrations are applied automatically at startup — no manual `dotnet ef database update`.
app.ApplyMigrations();

// Config-driven administrator record seed, aligned to the Identity admin credential via a shared
// IdentityId (idempotent; no-ops when "AdminSeed" is empty). Must run after migrations so the
// roles table is populated.
await AdminSeeder.SeedAdminAsync(app);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// GET /health — aggregated dependency status rendered as JSON (HealthChecks.UI.Client format).
app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Pushes trace/correlation ids into the Serilog LogContext so Seq logs link to Jaeger traces.
app.UseLogContext();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();

app.UseAuthorization();

// Maps every IEndpoint implementation discovered in the module's Presentation assembly —
// endpoints self-register; there is no manual route table.
app.MapEndpoints();

await app.RunAsync();

public partial class Program;
