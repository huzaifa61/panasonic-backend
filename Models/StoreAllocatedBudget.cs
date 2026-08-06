namespace PanasonicPrototype.Api.Models;

// A store's total approved budget envelope for a fiscal year. Unlike
// Store.Budget (which BudgetPlanningService auto-syncs to the GL-code line
// items' monthly sum on every save, and so can't act as an independent
// ceiling), this figure is set once via Store Management and never
// recalculated - it's the one number Budget Planning can validate the
// cross-line-item total against.
public class StoreAllocatedBudget
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }
    public int FiscalYear { get; set; }
    public decimal TotalAllocatedBudget { get; set; }
}
