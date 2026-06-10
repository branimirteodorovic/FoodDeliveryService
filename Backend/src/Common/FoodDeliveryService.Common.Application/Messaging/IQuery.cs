using FoodDeliveryService.Common.Domain;
using MediatR;

namespace FoodDeliveryService.Common.Application.Messaging;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
