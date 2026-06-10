using FoodDeliveryService.Common.Domain;
using MediatR;

namespace FoodDeliveryService.Common.Application.Messaging;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
