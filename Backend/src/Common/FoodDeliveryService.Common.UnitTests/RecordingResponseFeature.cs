using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace FoodDeliveryService.Common.UnitTests;

/// <summary>
/// A response feature that actually runs its <c>OnStarting</c> callbacks.
/// <para>
/// <see cref="DefaultHttpContext"/>'s own feature drops them on the floor, and both middlewares that
/// write response headers in this solution — <c>CorrelationIdMiddleware</c> and
/// <c>SecurityHeadersMiddleware</c> — write from that callback on purpose, so that a response reset
/// by the exception handler still carries them. Without this double, the property each one exists to
/// guarantee is the one property a unit test cannot see.
/// </para>
/// </summary>
internal sealed class RecordingResponseFeature : IHttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    public Stream Body { get; set; } = Stream.Null;

    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    public string? ReasonPhrase { get; set; }

    public bool HasStarted { get; private set; }

    public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        // Nothing under test registers one.
    }

    /// <summary>What a real server does when the first byte of the response goes out.</summary>
    public async Task FireOnStartingAsync()
    {
        HasStarted = true;

        foreach ((Func<object, Task> callback, object state) in _onStarting)
        {
            await callback(state);
        }
    }
}
