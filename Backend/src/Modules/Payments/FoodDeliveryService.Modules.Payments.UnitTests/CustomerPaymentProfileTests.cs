using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// The saved-card aggregate — Feature 3.8 Milestone D, §6.1.
/// <para>
/// Several of these cases are about events rather than state, and they are the ones worth having.
/// The attach and detach events drive Orders' replica of "this customer can pay by card"; an event
/// raised on a no-op makes that replica flap, and an event not raised on a real change leaves Orders
/// offering a card that is gone. Neither is visible from the row.
/// </para>
/// </summary>
public class CustomerPaymentProfileTests
{
    private const string StripeCustomerId = "cus_test_profile";

    private static readonly DateTime CreatedOn = new(2026, 9, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AttachedOn = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Should_LeaveTheCustomerWithoutACard()
    {
        // Act
        CustomerPaymentProfile profile = NewProfile();

        // Assert — the state every customer is in the moment they register. A profile is not a card.
        profile.HasPaymentMethod.Should().BeFalse();
        profile.StripeCustomerId.Should().Be(StripeCustomerId);
        profile.PaymentMethodId.Should().BeNull();
        profile.CreatedOnUtc.Should().Be(CreatedOn);
        profile.DomainEvents.Should().BeEmpty("nothing downstream cares about a customer with no card");
    }

    [Fact]
    public void Attach_Should_RecordTheCard_AndRaiseTheEvent()
    {
        // Arrange
        CustomerPaymentProfile profile = NewProfile();
        var paymentMethodId = Guid.CreateVersion7();

        // Act
        Result result = profile.Attach(paymentMethodId, "pm_test_visa", "visa", "4242", 12, 2030, AttachedOn);

        // Assert
        result.IsSuccess.Should().BeTrue();
        profile.HasPaymentMethod.Should().BeTrue();
        profile.PaymentMethodId.Should().Be(paymentMethodId);
        profile.StripePaymentMethodId.Should().Be("pm_test_visa");
        profile.Brand.Should().Be("visa");
        profile.Last4.Should().Be("4242");
        profile.AttachedOnUtc.Should().Be(AttachedOn);

        PaymentMethodAttachedDomainEvent raised =
            profile.DomainEvents.OfType<PaymentMethodAttachedDomainEvent>().Single();

        raised.CustomerId.Should().Be(profile.Id);
        raised.PaymentMethodId.Should().Be(paymentMethodId);
        raised.Brand.Should().Be("visa");
        raised.Last4.Should().Be("4242");
    }

    [Fact]
    public void Attach_Should_BeANoOp_WhenTheSameCardIsAttachedTwice()
    {
        // Arrange — the confirmation webhook is redelivered on any non-2xx (§7.4), so this is the
        // ordinary case rather than an edge one.
        CustomerPaymentProfile profile = NewProfile();
        var first = Guid.CreateVersion7();
        profile.Attach(first, "pm_test_visa", "visa", "4242", 12, 2030, AttachedOn);
        profile.ClearDomainEvents();

        // Act
        Result result = profile.Attach(
            Guid.CreateVersion7(),
            "pm_test_visa",
            "visa",
            "4242",
            12,
            2030,
            AttachedOn.AddMinutes(5));

        // Assert — the original id and timestamp survive, and nothing is republished. A second event
        // here would have every consumer re-project a change that did not happen.
        result.IsSuccess.Should().BeTrue();
        profile.PaymentMethodId.Should().Be(first);
        profile.AttachedOnUtc.Should().Be(AttachedOn);
        profile.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Attach_Should_ReplaceTheCard_WhenADifferentOneIsSaved()
    {
        // Arrange
        CustomerPaymentProfile profile = NewProfile();
        profile.Attach(Guid.CreateVersion7(), "pm_test_visa", "visa", "4242", 12, 2030, AttachedOn);
        profile.ClearDomainEvents();

        var replacement = Guid.CreateVersion7();

        // Act
        Result result = profile.Attach(
            replacement,
            "pm_test_mastercard",
            "mastercard",
            "4444",
            1,
            2031,
            AttachedOn.AddDays(1));

        // Assert — one card at a time (§6.1): saving another supersedes the first.
        result.IsSuccess.Should().BeTrue();
        profile.PaymentMethodId.Should().Be(replacement);
        profile.StripePaymentMethodId.Should().Be("pm_test_mastercard");
        profile.Last4.Should().Be("4444");
        profile.DomainEvents.OfType<PaymentMethodAttachedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Attach_Should_Fail_WhenThePaymentMethodIdIsBlank()
    {
        // Arrange
        CustomerPaymentProfile profile = NewProfile();

        // Act
        Result result = profile.Attach(Guid.CreateVersion7(), "  ", "visa", "4242", 12, 2030, AttachedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CustomerPaymentProfileErrors.PaymentMethodRequired);
        profile.HasPaymentMethod.Should().BeFalse();
    }

    [Fact]
    public void Detach_Should_ForgetTheCard_AndRaiseTheEvent()
    {
        // Arrange
        CustomerPaymentProfile profile = NewProfile();
        var paymentMethodId = Guid.CreateVersion7();
        profile.Attach(paymentMethodId, "pm_test_visa", "visa", "4242", 12, 2030, AttachedOn);
        profile.ClearDomainEvents();

        // Act
        Result result = profile.Detach(paymentMethodId, AttachedOn.AddHours(1));

        // Assert — every display field goes, not only the identifier. A row keeping "visa · 4242"
        // after the card is gone is a row that renders a card the customer removed.
        result.IsSuccess.Should().BeTrue();
        profile.HasPaymentMethod.Should().BeFalse();
        profile.PaymentMethodId.Should().BeNull();
        profile.Brand.Should().BeNull();
        profile.Last4.Should().BeNull();
        profile.ExpiryMonth.Should().BeNull();
        profile.ExpiryYear.Should().BeNull();
        profile.AttachedOnUtc.Should().BeNull();

        profile.DomainEvents.OfType<PaymentMethodDetachedDomainEvent>().Single()
            .PaymentMethodId.Should().Be(paymentMethodId);
    }

    [Fact]
    public void Detach_Should_Fail_WhenTheIdIsNotTheSavedCard()
    {
        // Arrange — a stale browser tab holding the id of a card that has since been replaced. The
        // id is checked precisely so this cannot delete the new one.
        CustomerPaymentProfile profile = NewProfile();
        profile.Attach(Guid.CreateVersion7(), "pm_test_visa", "visa", "4242", 12, 2030, AttachedOn);
        profile.ClearDomainEvents();

        var stale = Guid.CreateVersion7();

        // Act
        Result result = profile.Detach(stale, AttachedOn.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CustomerPaymentProfileErrors.PaymentMethodNotFound(stale));
        profile.HasPaymentMethod.Should().BeTrue();
        profile.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Detach_Should_Fail_WhenNoCardIsSaved()
    {
        // Arrange
        CustomerPaymentProfile profile = NewProfile();
        var paymentMethodId = Guid.CreateVersion7();

        // Act
        Result result = profile.Detach(paymentMethodId, AttachedOn);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CustomerPaymentProfileErrors.PaymentMethodNotFound(paymentMethodId));
        profile.DomainEvents.Should().BeEmpty();
    }

    private static CustomerPaymentProfile NewProfile() =>
        CustomerPaymentProfile.Create(Guid.CreateVersion7(), StripeCustomerId, CreatedOn);
}
