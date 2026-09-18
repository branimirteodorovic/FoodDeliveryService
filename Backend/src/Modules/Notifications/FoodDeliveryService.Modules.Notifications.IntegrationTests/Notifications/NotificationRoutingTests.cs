using AwesomeAssertions;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;

namespace FoodDeliveryService.Modules.Notifications.IntegrationTests.Notifications;

/// <summary>
/// The three halves of adding a notification type, asserted — Feature 3.8 Milestone H, §10.4.
/// <para>
/// A <see cref="NotificationType"/> needs an enum member, a template arm <b>and</b> a
/// <c>NotificationChannelRouter</c> route, and the third is the one that gets forgotten because
/// forgetting it is silent: <c>Resolve</c> returns an empty list, the send loop runs zero times, the
/// command returns success, the inbox message is marked processed, and the only symptom is an email
/// that never arrives. Nothing fails, nothing is logged, and nothing is retried.
/// </para>
/// <para>
/// These are structural rather than behavioural, so they need no containers — they live in this
/// project only because the router and the renderer are <c>internal</c>, and this is the assembly
/// they are visible to.
/// </para>
/// </summary>
public class NotificationRoutingTests
{
    [Fact]
    public void EveryNotificationType_Should_HaveAtLeastOneChannel()
    {
        NotificationType[] unroutable = Enum.GetValues<NotificationType>()
            .Where(type => NotificationChannelRouter.Resolve(type).Count == 0)
            .ToArray();

        unroutable.Should().BeEmpty(
            "a type missing from NotificationChannelRouter sends nothing and reports success; " +
            "add {0} to the Routes map",
            string.Join(", ", unroutable));
    }

    [Fact]
    public void EveryNotificationModel_Should_RenderATemplate()
    {
        var renderer = new NotificationTemplateRenderer();

        foreach (INotificationModel model in AllModels())
        {
            RenderedTemplate rendered = renderer.Render(model);

            rendered.Subject.Should().NotBeNullOrWhiteSpace(
                "a subject line is what a customer scans an inbox for ({0})",
                model.GetType().Name);
            rendered.Body.Should().NotBeNullOrWhiteSpace("{0} renders an empty email", model.GetType().Name);
        }
    }

    [Fact]
    public void EveryNotificationType_Should_HaveAModelThatDeclaresIt()
    {
        NotificationType[] declared = [.. AllModels().Select(m => m.Type).Distinct()];

        NotificationType[] modelless = Enum.GetValues<NotificationType>()
            .Except(declared)
            .ToArray();

        // Not a formality: the model is what carries the fields the template needs, so a type with
        // no model is a type nothing can ever actually send. If this fails because a model was added
        // and not listed below, the list is what needs updating — there is no reflection here on
        // purpose, so that adding a model is a deliberate act rather than an accident.
        modelless.Should().BeEmpty(
            "every NotificationType needs a model to render from; {0} has none",
            string.Join(", ", modelless));
    }

    /// <summary>One model per <see cref="NotificationType"/>, with values a template can format.</summary>
    private static IEnumerable<INotificationModel> AllModels()
    {
        yield return new OrderConfirmationModel("Ada", Guid.NewGuid(), 24.99m);
        yield return new SupportTicketReplyModel("Ada", "SUP-00000001", "Missing item", "We are looking into it");
        yield return new RefundDecisionModel("Ada", "SUP-00000001", 12.34m, Approved: true, "Confirmed");
        yield return new RefundDecisionModel("Ada", "SUP-00000001", 12.34m, Approved: false, DecisionNote: null);
        yield return new PaymentFailedModel("Ada", Guid.NewGuid(), "insufficient_funds");

        // The unmapped-reason arm: a code this module does not recognise must still produce an
        // email, because the customer's order has been cancelled either way.
        yield return new PaymentFailedModel("Ada", Guid.NewGuid(), "something_new_from_stripe");
        yield return new RefundSettledModel("Ada", "SUP-00000001", 12.34m);
    }
}
