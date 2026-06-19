using System.Reflection;
using FoodDeliveryService.Api.Extensions;
using FoodDeliveryService.Api.Middleware;
using FoodDeliveryService.Api.OpenTelemetry;
using FoodDeliveryService.Common.Application;
using FoodDeliveryService.Common.Infrastructure;
using FoodDeliveryService.Common.Infrastructure.Configuration;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Orders.Infrastructure;
using FoodDeliveryService.Modules.Restaurants.Infrastructure;
using FoodDeliveryService.Modules.Users.Infrastructure;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();

Assembly[] moduleApplicationAssemblies = [
    FoodDeliveryService.Modules.Orders.Application.AssemblyReference.Assembly,
    FoodDeliveryService.Modules.Restaurants.Application.AssemblyReference.Assembly,
    FoodDeliveryService.Modules.Users.Application.AssemblyReference.Assembly];

builder.Services.AddApplication(moduleApplicationAssemblies);

string databaseConnectionString = builder.Configuration.GetConnectionStringOrThrow("Database");
string redisConnectionString = builder.Configuration.GetConnectionStringOrThrow("Cache");
var rabbitMqSettings = new RabbitMqSettings(builder.Configuration.GetConnectionStringOrThrow("Queue"));

builder.Services.AddInfrastructure(
    DiagnosticsConfig.ServiceName,
    [
        //OrdersModule.ConfigureConsumers(redisConnectionString),
        UsersModule.ConfigureConsumers,
        //RestaurantsModule.ConfigureConsumers
    ],
    rabbitMqSettings,
    databaseConnectionString,
    redisConnectionString);

Uri keyCloakHealthUrl = builder.Configuration.GetKeyCloakHealthUrl();

builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString)
    .AddRedis(redisConnectionString)
    .AddRabbitMQ(sp => sp.GetRequiredService<IConnection>())
    .AddKeyCloak(keyCloakHealthUrl);

builder.Configuration.AddModuleConfiguration(["orders", "restaurants", "users"]);

#pragma warning disable S125 // Sections of code should not be commented out
                            //builder.Services.AddUsersModule(builder.Configuration);

//builder.Services.AddRestaurantsModule(builder.Configuration);

//builder.Services.AddOrdersModule(builder.Configuration);

WebApplication app = builder.Build();
#pragma warning restore S125 // Sections of code should not be commented out

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.ApplyMigrations();
}

app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.UseLogContext();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();

app.UseAuthorization();

app.MapEndpoints();

await app.RunAsync();

internal partial class Program;
