namespace PanasonicPrototype.Api.Models;

public class Revenue
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int CategoryId { get; set; }
    public ApplianceCategory? Category { get; set; }

    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }

    // Cost basis for this sale, used only to compute Margin % by Product. Kept separate
    // from the Expense/Budget/Approval system - it does not affect NetProfit or budget
    // variance, which remain Earnings minus operating Expense. Zero for manually-entered
    // revenue (no cost-entry field on that form); populated by seed data for demo rows.
    public decimal CostOfSales { get; set; }

    public string? Description { get; set; }
}
