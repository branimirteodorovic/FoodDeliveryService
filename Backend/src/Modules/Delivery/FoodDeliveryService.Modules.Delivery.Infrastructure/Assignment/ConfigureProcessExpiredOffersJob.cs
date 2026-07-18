using Microsoft.Extensions.Options;
using Quartz;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

internal sealed class ConfigureProcessExpiredOffersJob(IOptions<DeliveryAssignmentOptions> assignmentOptions)
    : IConfigureOptions<QuartzOptions>
{
    private readonly DeliveryAssignmentOptions _assignmentOptions = assignmentOptions.Value;

    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(ProcessExpiredOffersJob).FullName!;

        options
            .AddJob<ProcessExpiredOffersJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .WithSimpleSchedule(schedule =>
                        schedule
                            .WithIntervalInSeconds(_assignmentOptions.ExpiredOffersJobIntervalInSeconds)
                            .RepeatForever()));
    }
}
