namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// Scripts one gateway operation for the length of a test, and puts it back afterwards.
/// <para>
/// The fake is a singleton shared by the whole collection, and a scripted decline or fault that
/// outlives its test makes the next one fail for a reason that has nothing to do with it. A
/// <c>finally</c> would do the same job, but only if every early return and every failed assertion
/// is inside it — which is exactly the kind of thing that is right when written and wrong three
/// edits later.
/// </para>
/// <para>
/// <b>Scope it to the whole test method, not to the act.</b> These flows are asynchronous: the HTTP
/// call returns long before the inbox dispatches the handler that reaches the gateway, so a script
/// undone right after the request is a script that was never in force when it mattered.
/// </para>
/// </summary>
internal sealed class ScriptedGatewayOutcome : IDisposable
{
    private readonly FakePaymentGateway _gateway;
    private readonly FakeGatewayOperation _operation;
    private readonly string _previousDeclineReason;

    public ScriptedGatewayOutcome(
        FakePaymentGateway gateway,
        FakeGatewayOperation operation,
        FakeGatewayOutcome outcome,
        string? declineReason = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        _gateway = gateway;
        _operation = operation;
        _previousDeclineReason = gateway.DeclineReason;

        if (declineReason is not null)
        {
            gateway.DeclineReason = declineReason;
        }

        gateway.Script(operation, outcome);
    }

    public void Dispose()
    {
        _gateway.Script(_operation, FakeGatewayOutcome.Succeed);
        _gateway.DeclineReason = _previousDeclineReason;
    }
}
