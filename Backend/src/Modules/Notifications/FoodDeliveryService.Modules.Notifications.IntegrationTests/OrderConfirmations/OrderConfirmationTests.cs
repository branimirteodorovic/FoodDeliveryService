using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Database;
using FoodDeliveryService.Modules.Notifications.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Notifications.IntegrationTests.OrderConfirmations;

public class OrderConfirmationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task OrderPlaced_Should_LogSentOrderConfirmationNotification()
    {
        // Arrange — the recipient replica must exist before the confirmation handler can resolve the
        // customer's email address.
        SeededRecipient recipient = await SeedRecipientAsync(TestContext.Current.CancellationToken);

        // Act — publish the order event; Notifications resolves the recipient and sends the
        // (dev-logged) confirmation email, recording the send as one audit-log row.
        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId: Guid.NewGuid(),
                customerId: recipient.UserId,
                restaurantId: Guid.NewGuid(),
                subtotal: 42.50m,
                placedOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — poll until a Sent notification for this recipient is logged.
        Result<Notification> notification = await WaitForNotificationAsync(
            n => n.RecipientUserId == recipient.UserId && n.Status == NotificationStatus.Sent);

        notification.IsSuccess.Should().BeTrue("placing an order should log a Sent order-confirmation notification");

        Notification row = notification.Value;
        row.RecipientEmail.Should().Be(recipient.Email);
        row.RecipientUserId.Should().Be(recipient.UserId);
        row.Type.Should().Be(NotificationType.OrderConfirmation);
        row.Channel.Should().Be(NotificationChannel.Email);
        row.Status.Should().Be(NotificationStatus.Sent);
        row.SentOnUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task OrderPlaced_Should_NotLogNotification_WhenRecipientReplicaMissing()
    {
        // Arrange — a customer with no RecipientUser replica (no UserRegistered event was published).
        var unknownCustomerId = Guid.NewGuid();

        // Act
        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId: Guid.NewGuid(),
                customerId: unknownCustomerId,
                restaurantId: Guid.NewGuid(),
                subtotal: 12.00m,
                placedOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — the handler fails fast (recipient not found) *before* creating any row, and the
        // inbox keeps retrying rather than dropping the message, so no notification is ever logged.
        // The poller only "succeeds" when a row appears; a timeout here is therefore the pass signal.
        Result<Notification> notification = await Poller.WaitAsync<Notification>(
            TimeSpan.FromSeconds(10),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

                return await context.Set<Notification>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        n => n.RecipientUserId == unknownCustomerId,
                        TestContext.Current.CancellationToken);
            });

        notification.IsFailure.Should().BeTrue("no notification row should be logged when the recipient replica is missing");
    }

    [Fact]
    public async Task OrderPlaced_DuplicateDelivery_Should_LogSingleNotification()
    {
        // Arrange
        SeededRecipient recipient = await SeedRecipientAsync(TestContext.Current.CancellationToken);

        // The inbox is keyed on the integration event's Id, so re-delivering the *same* event is
        // idempotent — it must never produce a second email / audit row.
        var orderPlaced = new OrderPlacedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            orderId: Guid.NewGuid(),
            customerId: recipient.UserId,
            restaurantId: Guid.NewGuid(),
            subtotal: 19.99m,
            placedOnUtc: DateTime.UtcNow);

        // Act — publish the identical event twice.
        await Factory.PublishAsync(orderPlaced, TestContext.Current.CancellationToken);
        await Factory.PublishAsync(orderPlaced, TestContext.Current.CancellationToken);

        // Assert — wait for the confirmation to be logged, then confirm exactly one row exists.
        Result<Notification> notification = await WaitForNotificationAsync(
            n => n.RecipientUserId == recipient.UserId && n.Status == NotificationStatus.Sent);

        notification.IsSuccess.Should().BeTrue("the order confirmation should be logged once");

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        int count = await context.Set<Notification>()
            .AsNoTracking()
            .CountAsync(
                n => n.RecipientUserId == recipient.UserId,
                TestContext.Current.CancellationToken);

        count.Should().Be(1, "duplicate delivery of the same order event must be deduplicated by the inbox");
    }

    private async Task<Result<Notification>> WaitForNotificationAsync(
        System.Linq.Expressions.Expression<Func<Notification, bool>> predicate) =>
        await Poller.WaitAsync<Notification>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

                // Notification? → Result<Notification>: null converts to Failure(Error.NullValue),
                // which keeps the poller retrying until the send is logged.
                return await context.Set<Notification>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(predicate, TestContext.Current.CancellationToken);
            });
}
