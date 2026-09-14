using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.MarkWebhookEventProcessed;

internal sealed class MarkWebhookEventProcessedCommandHandler(
    IStripeEventLogRepository eventLogRepository,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<MarkWebhookEventProcessedCommand>
{
    public async Task<Result> Handle(
        MarkWebhookEventProcessedCommand request,
        CancellationToken cancellationToken)
    {
        StripeEventLog? eventLog = await eventLogRepository.GetAsync(request.EventLogId, cancellationToken);

        if (eventLog is null)
        {
            return Result.Failure(StripeEventLogErrors.NotFound(request.EventLogId));
        }

        if (request.Error is null)
        {
            eventLog.MarkProcessed(dateTimeProvider.UtcNow);
        }
        else
        {
            eventLog.MarkFailed(request.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
