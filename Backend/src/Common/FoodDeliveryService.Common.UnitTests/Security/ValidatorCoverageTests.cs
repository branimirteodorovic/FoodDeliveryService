using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using DeliveryApplication = FoodDeliveryService.Modules.Delivery.Application;
using NotificationsApplication = FoodDeliveryService.Modules.Notifications.Application;
using OrdersApplication = FoodDeliveryService.Modules.Orders.Application;
using RealTimeApplication = FoodDeliveryService.Modules.RealTime.Application;
using RestaurantsApplication = FoodDeliveryService.Modules.Restaurants.Application;
using SupportApplication = FoodDeliveryService.Modules.Support.Application;
using UsersApplication = FoodDeliveryService.Modules.Users.Application;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone F. <c>ValidationPipelineBehavior</c> <b>silently no-ops</b> for a request
/// with no <see cref="AbstractValidator{T}"/>: it resolves the empty
/// <c>IEnumerable&lt;IValidator&lt;T&gt;&gt;</c> and calls the handler. So a command that ships
/// without a validator is not a broken build, a failing test or a log line — it is an endpoint that
/// accepts whatever the caller sent, and it looks exactly like the one next to it that validates.
/// <para>
/// This suite closes that gap for the requests carrying <em>user</em> input: everything an HTTP
/// endpoint can reach. Requests driven only by an integration-event handler or a Quartz job are out
/// of scope on purpose — their fields come from a replicated event that was validated at its source,
/// and a validation failure there is not a 400 to a caller but a dropped replica
/// (<c>ProcessInboxJob</c> records the error and marks the message processed; it does not retry).
/// That departure from the plan's literal "every request" is written up in
/// <c>HARDENING_PHASE3_PLAN.md</c> §7.1.
/// </para>
/// <para>
/// Reachability is read from the source of the <c>IEndpoint</c> implementations rather than listed
/// here, so a new endpoint is covered the moment it is written — the same reason
/// <see cref="SecurityHeaderCoverageTests"/> enumerates host directories instead of naming them.
/// </para>
/// </summary>
public partial class ValidatorCoverageTests
{
    /// <summary>
    /// Endpoint-reachable requests that legitimately have no validator, because they carry nothing a
    /// validator could reject.
    /// <para>
    /// Needing to edit this list is the point of it, exactly as with
    /// <c>EndpointAuthorizationTests.AnonymousRoutes</c>: an unvalidated command cannot arrive in a
    /// different pull request from its exemption.
    /// <see cref="ExemptRequests_Should_HaveNoValidatableField"/> checks the claim rather than
    /// trusting it, so an entry that grows a string or an id fails here.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> RequestsWithNothingToValidate = new(StringComparer.Ordinal)
    {
        ["SetDriverAvailabilityCommand"] =
            "One bool. The driver is the authenticated caller, so there is no id to tamper with, and " +
            "both values of the flag are legitimate."
    };

    /// <summary>
    /// The seven module Application assemblies. RealTime is carried with
    /// <c>DeclaresRequests: false</c> because it has no commands or queries at all — its endpoint is
    /// a SignalR hub — so "nothing found" reads as a decision rather than as a broken scan.
    /// </summary>
    private static readonly ModuleApplication[] Modules =
    [
        new("Delivery", DeliveryApplication.AssemblyReference.Assembly, DeclaresRequests: true),
        new("Notifications", NotificationsApplication.AssemblyReference.Assembly, DeclaresRequests: true),
        new("Orders", OrdersApplication.AssemblyReference.Assembly, DeclaresRequests: true),
        new("RealTime", RealTimeApplication.AssemblyReference.Assembly, DeclaresRequests: false),
        new("Restaurants", RestaurantsApplication.AssemblyReference.Assembly, DeclaresRequests: true),
        new("Support", SupportApplication.AssemblyReference.Assembly, DeclaresRequests: true),
        new("Users", UsersApplication.AssemblyReference.Assembly, DeclaresRequests: true)
    ];

    /// <summary>Longer than any legitimate field, short enough to keep the test instant.</summary>
    private const int OverlongText = 10_001;

    /// <summary>
    /// The free denial of service this milestone exists to close: one request, one round trip, an
    /// unbounded scan — which the edge rate limiter charges as a single call.
    /// </summary>
    private const int AbusivePageSize = 1_000_000;

    /// <summary>Modules that must keep at least one bounded paged endpoint.</summary>
    private static readonly string[] PagedModules = ["Delivery", "Orders", "Restaurants", "Support"];

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();

        foreach (ModuleApplication module in Modules)
        {
            data.Add(module.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryModule_Should_ExposeItsRequests_ThroughReflection(string moduleName)
    {
        // Arrange
        ModuleApplication module = Module(moduleName);

        // Act — the vacuity guard. Without it, a change to how requests are declared would empty
        // every collection below and every assertion in this file would pass over nothing.
        IReadOnlyList<Type> requests = RequestTypes(module.Assembly);

        // Assert
        if (module.DeclaresRequests)
        {
            requests.Should().NotBeEmpty("{0}.Application declares ICommand/IQuery types", moduleName);
        }
        else
        {
            requests.Should().BeEmpty(
                "{0} has no CQRS surface — a request here is one nobody decided to add", moduleName);
        }
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryEndpointReachableRequest_Should_HaveAValidator(string moduleName)
    {
        // Arrange
        ModuleApplication module = Module(moduleName);
        Dictionary<Type, Type> validators = ValidatorsByRequest(module.Assembly);

        // Act
        IReadOnlyList<string> unvalidated =
        [
            .. EndpointReachableRequests(module)
                .Where(request => !validators.ContainsKey(request))
                .Select(request => request.Name)
                .Where(name => !RequestsWithNothingToValidate.ContainsKey(name))
        ];

        // Assert
        unvalidated.Should().BeEmpty(
            "every request an HTTP caller can reach in {0} must be validated at the boundary — " +
            "ValidationPipelineBehavior no-ops for the ones that are not, so the omission is " +
            "invisible at runtime. Add an AbstractValidator, or list it in " +
            "RequestsWithNothingToValidate with the reason.",
            moduleName);
    }

    [Fact]
    public void ExemptRequests_Should_StillBeEndpointReachable()
    {
        // Arrange — the reverse direction. An exemption whose request was deleted, or which was
        // later given a validator, is a stale entry waiting to be reused by accident.
        IReadOnlyList<string> unvalidatedAndReachable =
        [
            .. Modules.SelectMany(module => EndpointReachableRequests(module)
                .Where(request => !ValidatorsByRequest(module.Assembly).ContainsKey(request))
                .Select(request => request.Name))
        ];

        // Assert
        RequestsWithNothingToValidate.Keys.Should().BeEquivalentTo(unvalidatedAndReachable);
    }

    [Fact]
    public void ExemptRequests_Should_HaveNoValidatableField()
    {
        // Arrange — "nothing a validator could reject" means no string, no id and no number. A bool
        // or an enum is already total: every value it can hold is one the handler must accept.
        var offenders = new List<string>();

        foreach (Type request in Modules.SelectMany(EndpointReachableRequests))
        {
            if (!RequestsWithNothingToValidate.ContainsKey(request.Name))
            {
                continue;
            }

            offenders.AddRange(
                request
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => IsValidatable(property.PropertyType))
                    .Select(property => $"{request.Name}.{property.Name}"));
        }

        // Assert
        offenders.Should().BeEmpty(
            "a request on the exemption list grew a field the caller controls — it needs a validator " +
            "now, and the exemption needs deleting");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryValidator_Should_TargetARequestOfItsOwnModule(string moduleName)
    {
        // Arrange
        ModuleApplication module = Module(moduleName);
        var requests = RequestTypes(module.Assembly).ToHashSet();

        // Act — a validator for a type that is no longer a request is never resolved by the
        // behavior and never runs. It reads like coverage and provides none.
        IReadOnlyList<string> orphans =
        [
            .. ValidatorsByRequest(module.Assembly)
                .Where(pair => !requests.Contains(pair.Key))
                .Select(pair => pair.Value.Name)
        ];

        // Assert
        orphans.Should().BeEmpty("a validator whose subject is not a request in {0} never runs", moduleName);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryFreeTextField_Should_BeLengthBounded(string moduleName)
    {
        // Arrange
        ModuleApplication module = Module(moduleName);
        Dictionary<Type, Type> validators = ValidatorsByRequest(module.Assembly);
        var offenders = new List<string>();

        foreach (Type request in EndpointReachableRequests(module))
        {
            if (!validators.TryGetValue(request, out Type? validatorType))
            {
                continue;
            }

            foreach (ParameterInfo parameter in InputParameters(request, type => type == typeof(string)))
            {
                // Act — the request is built with this one field overlong and everything else at its
                // default. Other rules fail too; only a failure reported against THIS property
                // proves THIS property is bounded.
                ValidationResult result = Validate(
                    validatorType,
                    Create(request, parameter.Name!, new string('a', OverlongText)));

                if (!result.Errors.Any(failure => failure.PropertyName == PropertyName(parameter)))
                {
                    offenders.Add($"{request.Name}.{PropertyName(parameter)}");
                }
            }
        }

        // Assert — an unbounded free-text field is a row the caller sizes: it reaches the database,
        // the outbox message, every consumer's replica and, for a name, an outgoing email.
        offenders.Should().BeEmpty(
            "every free-text field on {0}'s reachable requests needs a MaximumLength — or a rule that " +
            "rejects an overlong value some other way, such as an enum parse",
            moduleName);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryPagedRequest_Should_RejectAnAbusivePageSize(string moduleName)
    {
        // Arrange
        ModuleApplication module = Module(moduleName);
        Dictionary<Type, Type> validators = ValidatorsByRequest(module.Assembly);
        var offenders = new List<string>();
        var pagedFields = 0;

        foreach (Type request in EndpointReachableRequests(module))
        {
            foreach (ParameterInfo parameter in InputParameters(request, type => type == typeof(int)))
            {
                int? abusive = PropertyName(parameter) switch
                {
                    "PageSize" => AbusivePageSize,

                    // Page 0 is the other half of the same bound: the handler turns it into a
                    // negative OFFSET, which Postgres rejects with a 500.
                    "Page" => 0,
                    _ => null
                };

                if (abusive is null)
                {
                    continue;
                }

                pagedFields++;

                // Act
                if (!validators.TryGetValue(request, out Type? validatorType))
                {
                    offenders.Add($"{request.Name}.{PropertyName(parameter)} (no validator at all)");
                    continue;
                }

                ValidationResult result = Validate(
                    validatorType,
                    Create(request, parameter.Name!, abusive.Value));

                if (!result.Errors.Any(failure => failure.PropertyName == PropertyName(parameter)))
                {
                    offenders.Add($"{request.Name}.{PropertyName(parameter)}");
                }
            }
        }

        // Assert
        offenders.Should().BeEmpty(
            "an unbounded page size is a full-table read the caller asks for in a single request, " +
            "which the edge rate limiter charges as a single request");

        if (PagedModules.Contains(moduleName))
        {
            pagedFields.Should().BePositive("{0} has paged endpoints to bound", moduleName);
        }
    }

    private static ModuleApplication Module(string name) =>
        Modules.Single(module => module.Name == name);

    /// <summary>
    /// Every concrete MediatR request in the assembly — <c>ICommand</c>, <c>ICommand&lt;T&gt;</c>,
    /// <c>IQuery&lt;T&gt;</c> and <c>ICachedQuery&lt;T&gt;</c> all reduce to <c>IBaseRequest</c>.
    /// </summary>
    private static IReadOnlyList<Type> RequestTypes(Assembly assembly) =>
    [
        .. assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(IBaseRequest).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
    ];

    private static Dictionary<Type, Type> ValidatorsByRequest(Assembly assembly)
    {
        var validators = new Dictionary<Type, Type>();

        foreach (Type type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false }))
        {
            Type? subject = ValidatedType(type);

            if (subject is not null)
            {
                validators[subject] = type;
            }
        }

        return validators;
    }

    private static Type? ValidatedType(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// The requests an HTTP caller can reach, read from the module's <c>IEndpoint</c> sources.
    /// <para>
    /// It matches any <c>…Command</c>/<c>…Query</c> identifier mentioned in one of those files rather
    /// than only <c>new X(</c>, because an endpoint may build its request through a factory —
    /// <c>GetSupportSummaryQuery.Create(…)</c> does exactly that, and a constructor-only scan would
    /// have quietly stopped covering it.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Type> EndpointReachableRequests(ModuleApplication module)
    {
        HashSet<string> mentioned = MentionedInEndpoints(module.Name);

        return [.. RequestTypes(module.Assembly).Where(request => mentioned.Contains(request.Name))];
    }

    private static HashSet<string> MentionedInEndpoints(string moduleName)
    {
        string presentation = RepositoryPaths.Backend(
            "src",
            "Modules",
            moduleName,
            $"FoodDeliveryService.Modules.{moduleName}.Presentation");

        var mentioned = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);

            if (!source.Contains(": IEndpoint", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in RequestIdentifier().Matches(source))
            {
                mentioned.Add(match.Value);
            }
        }

        return mentioned;
    }

    /// <summary>
    /// The constructor parameters a caller fills in, for the widest constructor — which for a
    /// positional record is the one the endpoint uses.
    /// </summary>
    private static IEnumerable<ParameterInfo> InputParameters(Type request, Func<Type, bool> ofType) =>
        PrimaryConstructor(request)
            .GetParameters()
            .Where(parameter => ofType(Underlying(parameter.ParameterType)));

    private static ConstructorInfo PrimaryConstructor(Type request) =>
        request
            .GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First();

    private static object Create(Type request, string parameterName, object value)
    {
        ConstructorInfo constructor = PrimaryConstructor(request);

        object?[] arguments =
        [
            .. constructor
                .GetParameters()
                .Select(parameter => string.Equals(parameter.Name, parameterName, StringComparison.Ordinal)
                    ? value
                    : Default(parameter.ParameterType))
        ];

        return constructor.Invoke(arguments);
    }

    private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static ValidationResult Validate(Type validatorType, object instance)
    {
        var validator = (IValidator)Activator.CreateInstance(validatorType)!;

        return validator.Validate(new ValidationContext<object>(instance));
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>
    /// FluentValidation reports failures against the PROPERTY name; a positional record's
    /// constructor parameter is camel-cased. The two differ only in the first letter.
    /// </summary>
    private static string PropertyName(ParameterInfo parameter) =>
        char.ToUpperInvariant(parameter.Name![0]) + parameter.Name[1..];

    private static bool IsValidatable(Type type)
    {
        Type underlying = Underlying(type);

        return underlying == typeof(string) ||
               underlying == typeof(Guid) ||
               underlying == typeof(int) ||
               underlying == typeof(long) ||
               underlying == typeof(decimal) ||
               underlying == typeof(double);
    }

    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9_]*(?:Command|Query)\b")]
    private static partial Regex RequestIdentifier();

    private sealed record ModuleApplication(string Name, Assembly Assembly, bool DeclaresRequests);
}
