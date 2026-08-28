namespace FoodDeliveryService.Modules.Support.Domain.Audit;

/// <summary>
/// Insert only. There is no Get, no Update and no Delete on purpose: reads go through the Dapper
/// audit query like every other read in this codebase, and the two missing verbs are what makes the
/// log append-only in the type system rather than only in the documentation.
/// </summary>
public interface ISupportAuditRepository
{
    void Insert(SupportAuditEntry entry);
}
