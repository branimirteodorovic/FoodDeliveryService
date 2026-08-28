namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// Where the ticket came from. Two members are deliberately unreachable in this feature and exist
/// so their producers land as pure additions rather than as an enum migration:
/// <see cref="Chatbot"/> waits on the AI assistant (Feature 3.1/3.2), and <see cref="FraudFlag"/>
/// on a FraudDetection service that was built and reverted (6ae4879) and does not exist in the
/// tree. Nothing in Support writes either one.
/// </summary>
public enum TicketSource
{
    CustomerPortal = 0,
    AgentCreated = 1,
    Chatbot = 2,
    FraudFlag = 3
}
