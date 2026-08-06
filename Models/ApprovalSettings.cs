namespace PanasonicPrototype.Api.Models;

// Singleton row (global, applies across all stores) - not tied to any store,
// year, or month. Any single expense above this amount requires approval,
// in addition to the existing budget-overage rule.
public class ApprovalSettings
{
    public int Id { get; set; }
    public decimal ExpenseApprovalThreshold { get; set; }
}
