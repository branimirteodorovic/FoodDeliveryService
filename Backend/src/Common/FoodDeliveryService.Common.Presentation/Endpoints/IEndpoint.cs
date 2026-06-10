using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Common.Presentation.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
