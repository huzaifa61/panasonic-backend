namespace PanasonicPrototype.Api.Models;

public enum ExpenseStatus
{
    Approved,
    PendingLevel1Approval,
    PendingLevel2Approval,
    Rejected
}

public class Expense
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int CategoryId { get; set; }
    public ApplianceCategory? Category { get; set; }

    public int? BudgetLineItemId { get; set; }
    public BudgetLineItem? BudgetLineItem { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public ExpenseStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Approval> Approvals { get; set; } = new();
}
