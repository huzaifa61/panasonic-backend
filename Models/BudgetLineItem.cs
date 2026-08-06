namespace PanasonicPrototype.Api.Models;

public class BudgetLineItem
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    // "Starts-in" convention: FiscalYear = 2026 means the Apr-2026 -> Mar-2027 fiscal year.
    public int FiscalYear { get; set; }

    public string GLCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CostCenter { get; set; } = string.Empty;
    public string ExpenseCategory { get; set; } = string.Empty;

    public decimal Units { get; set; }
    public decimal Rate { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int NoOfMonths { get; set; }

    // Computed server-side as Units * Rate whenever those inputs change - not user-typed directly.
    public decimal TotalCost { get; set; }

    // A separately negotiated ceiling, distinct from TotalCost - the validation/approval-gating threshold.
    public decimal ApprovedAmount { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<BudgetLineItemMonth> Months { get; set; } = new();
}
