using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FoodDeliveryService.Common.UnitTests.Security;

/// <summary>
/// Feature 3.7 Milestone F §7.4. Every failing request on this platform leaves through
/// <see cref="ApiResults.Problem"/>, so what that method chooses to put in the body is the whole
/// error surface a caller ever sees. The property worth holding is narrow: a failure the caller
/// caused is described to them, and a failure they did not cause is not.
/// <para>
/// The distinction is <see cref="ErrorType"/>. Validation/Problem/NotFound/Conflict are the caller's
/// own request coming back at them — an id they sent, a status transition they asked for — and the
/// description is the useful part. <see cref="ErrorType.Failure"/> is everything else, and its
/// description is written for an operator: it is the arm that would otherwise carry an Npgsql
/// message, a connection string fragment or a row's contents to whoever typed the URL.
/// </para>
/// </summary>
public class ErrorSurfaceTests
{
    /// <summary>
    /// Stands in for the kinds of thing a <see cref="ErrorType.Failure"/> description picks up when
    /// it is built from an exception: a host name, a credential, a row.
    /// </summary>
    private const string Sensitive =
        "Npgsql.PostgresException: relation \"users\" — Host=10.0.0.4;Password=hunter2";

    [Fact]
    public void Problem_Should_NotEchoTheDescription_ForAnInternalFailure()
    {
        // Arrange
        var result = Result.Failure(Error.Failure("Users.Unexpected", Sensitive));

        // Act
        var problem = Problem(result);

        // Assert — neither field, and neither by accident: the title is the error CODE for the four
        // caller-facing arms, which would put the internal code on the wire here too.
        problem.ProblemDetails.Detail.Should().Be("An unexpected error occurred");
        problem.ProblemDetails.Title.Should().Be("Server failure");
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Problem, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public void Problem_Should_DescribeTheFailure_WhenTheCallerCausedIt(ErrorType type, int expectedStatus)
    {
        // Arrange — the other half of the property. A 404 that says nothing is not safer, it is
        // just unusable, and it pushes the caller into retrying until something works.
        var result = Result.Failure(new Error("Orders.NotFound", "The order was not found", type));

        // Act
        var problem = Problem(result);

        // Assert
        problem.StatusCode.Should().Be(expectedStatus);
        problem.ProblemDetails.Detail.Should().Be("The order was not found");
    }

    [Fact]
    public void Problem_Should_NotCarryAnException_InAnyForm()
    {
        // Arrange — ApiResults takes a Result, never an exception, so there is no parameter through
        // which a stack trace could arrive. This asserts the consequence rather than the shape: the
        // rendered body has no exception, no stack trace and no inner-exception extension, whatever
        // the pipeline caught upstream.
        var result = Result.Failure(Error.Failure("Users.Unexpected", Sensitive));

        // Act
        var problem = Problem(result);

        // Assert
        problem.ProblemDetails.Extensions.Should().NotContainKeys("exception", "stackTrace", "innerException");
        problem.ProblemDetails.Instance.Should().BeNull();
    }

    [Fact]
    public void Problem_Should_ListValidationErrors_ForAValidationFailure()
    {
        // Arrange — the one place extensions ARE populated, and the reason the assertion above names
        // keys instead of demanding an empty dictionary.
        var validationError = new ValidationError(
            [Error.Problem("PageSize", "'Page Size' must be between 1 and 100.")]);

        // Act
        var problem = Problem(Result.Failure(validationError));

        // Assert
        problem.ProblemDetails.Extensions.Should().ContainKey("errors");
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Problem_Should_Throw_WhenHandedASuccess()
    {
        // Arrange — a success reaching the failure branch is a bug in the endpoint, and failing
        // loudly in tests beats rendering a 500 that says nothing to a caller whose call worked.
        Action problem = () => ApiResults.Problem(Result.Success());

        // Assert
        problem.Should().Throw<InvalidOperationException>();
    }

    private static ProblemHttpResult Problem(Result result) => (ProblemHttpResult)ApiResults.Problem(result);
}
