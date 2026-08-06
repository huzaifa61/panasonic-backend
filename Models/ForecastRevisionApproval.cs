namespace PanasonicPrototype.Api.Models;

public class ForecastRevisionApproval
{
    public int Id { get; set; }

    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int BudgetLineItemId { get; set; }
    public BudgetLineItem? BudgetLineItem { get; set; }

    public int CalendarYear { get; set; }
    public int CalendarMonth { get; set; }

    // The revision isn't committed to ForecastVersionLine until approved, so the
    // proposed (and prior, for display/audit) values live here instead.
    public decimal ProposedAmount { get; set; }
    public decimal PreviousAmount { get; set; }

    // 1 = Store Manager, 2 = Regional Manager
    public int Level { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public string? ApproverName { get; set; }

    public ApprovalDecision Decision { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comments { get; set; }
    public DateTime CreatedAt { get; set; }
}
