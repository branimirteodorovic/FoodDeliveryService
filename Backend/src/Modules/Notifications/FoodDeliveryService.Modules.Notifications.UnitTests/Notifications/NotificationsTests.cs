using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Notifications.UnitTests.Notifications;

public class NotificationsTests : BaseTest
{
    [Fact]
    public void Create_ShouldSetPendingStatusAndFields_WhenValid()
    {
        // Arrange
        var recipientEmail = Faker.Person.Email;
        var recipientUserId = Guid.NewGuid();
        var subject = Faker.Lorem.Sentence();
        var createdOnUtc = DateTime.UtcNow;

        // Act
        Result<Notification> result = Notification.Create(
            recipientEmail,
            recipientUserId,
            NotificationType.OrderConfirmation,
            NotificationChannel.Email,
            subject,
            createdOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        Notification notification = result.Value;
        notification.RecipientEmail.Should().Be(recipientEmail);
        notification.RecipientUserId.Should().Be(recipientUserId);
        notification.Type.Should().Be(NotificationType.OrderConfirmation);
        notification.Channel.Should().Be(NotificationChannel.Email);
        notification.Subject.Should().Be(subject);
        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.CreatedOnUtc.Should().Be(createdOnUtc);
        notification.SentOnUtc.Should().BeNull();
        notification.Error.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldRaiseDomainEvent_WhenNotificationIsCreated()
    {
        // Arrange & Act
        Notification notification = CreateNotification();

        // Assert
        NotificationCreatedDomainEvent domainEvent =
            AssertDomainEventWasPublished<NotificationCreatedDomainEvent>(notification);
        domainEvent.NotificationId.Should().Be(notification.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldReturnFailure_WhenRecipientEmailIsEmpty(string? recipientEmail)
    {
        // Act
        Result<Notification> result = Notification.Create(
            recipientEmail!,
            Guid.NewGuid(),
            NotificationType.OrderConfirmation,
            NotificationChannel.Email,
            Faker.Lorem.Sentence(),
            DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationErrors.RecipientEmailEmpty);
    }

    [Fact]
    public void Create_ShouldAllowNullRecipientUserId_WhenRecipientIsNotAReplica()
    {
        // Act
        Result<Notification> result = Notification.Create(
            Faker.Person.Email,
            recipientUserId: null,
            NotificationType.OrderConfirmation,
            NotificationChannel.Email,
            Faker.Lorem.Sentence(),
            DateTime.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RecipientUserId.Should().BeNull();
    }

    [Fact]
    public void MarkSent_ShouldTransitionToSentAndRaiseDomainEvent_WhenPending()
    {
        // Arrange
        Notification notification = CreateNotification();
        var sentOnUtc = DateTime.UtcNow;

        // Act
        Result result = notification.MarkSent(sentOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.SentOnUtc.Should().Be(sentOnUtc);
        notification.Error.Should().BeNull();
        NotificationSentDomainEvent domainEvent =
            AssertDomainEventWasPublished<NotificationSentDomainEvent>(notification);
        domainEvent.NotificationId.Should().Be(notification.Id);
    }

    [Fact]
    public void MarkSent_ShouldReturnFailure_WhenAlreadySent()
    {
        // Arrange
        Notification notification = CreateNotification();
        notification.MarkSent(DateTime.UtcNow);

        // Act
        Result result = notification.MarkSent(DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            NotificationErrors.InvalidTransition(NotificationStatus.Sent, NotificationStatus.Sent));
    }

    [Fact]
    public void MarkSent_ShouldReturnFailure_WhenAlreadyFailed()
    {
        // Arrange
        Notification notification = CreateNotification();
        notification.MarkFailed("smtp down");

        // Act
        Result result = notification.MarkSent(DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            NotificationErrors.InvalidTransition(NotificationStatus.Failed, NotificationStatus.Sent));
    }

    [Fact]
    public void MarkFailed_ShouldTransitionToFailedAndRaiseDomainEvent_WhenPending()
    {
        // Arrange
        Notification notification = CreateNotification();
        var error = Faker.Lorem.Sentence();

        // Act
        Result result = notification.MarkFailed(error);

        // Assert
        result.IsSuccess.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.Error.Should().Be(error);
        notification.SentOnUtc.Should().BeNull();
        NotificationFailedDomainEvent domainEvent =
            AssertDomainEventWasPublished<NotificationFailedDomainEvent>(notification);
        domainEvent.NotificationId.Should().Be(notification.Id);
    }

    [Fact]
    public void MarkFailed_ShouldReturnFailure_WhenAlreadyFailed()
    {
        // Arrange
        Notification notification = CreateNotification();
        notification.MarkFailed("first failure");

        // Act
        Result result = notification.MarkFailed("second failure");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            NotificationErrors.InvalidTransition(NotificationStatus.Failed, NotificationStatus.Failed));
    }

    [Fact]
    public void MarkFailed_ShouldReturnFailure_WhenAlreadySent()
    {
        // Arrange
        Notification notification = CreateNotification();
        notification.MarkSent(DateTime.UtcNow);

        // Act
        Result result = notification.MarkFailed("too late");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(
            NotificationErrors.InvalidTransition(NotificationStatus.Sent, NotificationStatus.Failed));
    }

    private static Notification CreateNotification()
    {
        return Notification.Create(
            Faker.Person.Email,
            Guid.NewGuid(),
            NotificationType.OrderConfirmation,
            NotificationChannel.Email,
            Faker.Lorem.Sentence(),
            DateTime.UtcNow).Value;
    }
}
