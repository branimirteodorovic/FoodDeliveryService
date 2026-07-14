using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Provisioning;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Fakes;

/// <summary>
/// Replaces the real MassTransit request/response implementation in tests. The Users test host
/// (see UsersApiTestFactory) does register a consumer for ProvisionManagerUserRequest, but this
/// fake keeps manager-onboarding tests independent of that flow — swap it out if/when those tests
/// need to exercise the real provisioning RPC too.
/// </summary>
internal sealed class FakeManagerProvisioningService : IManagerProvisioningService
{
    public Task<Result<Guid>> ProvisionManagerAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(Guid.NewGuid()));

    public Task<Result> DeactivateManagerAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
