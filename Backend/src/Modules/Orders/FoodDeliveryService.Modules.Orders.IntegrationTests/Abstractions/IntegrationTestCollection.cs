namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

[CollectionDefinition(nameof(IntegrationTestCollection))]
#pragma warning disable CA1515 // Consider making public types internal
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestWebAppFactory>;
#pragma warning restore CA1515 // Consider making public types internal
