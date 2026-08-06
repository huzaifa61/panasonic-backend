namespace PanasonicPrototype.Api.Models;

public class BudgetLineItemMonth
{
    public int Id { get; set; }
    public int BudgetLineItemId { get; set; }
    public BudgetLineItem? BudgetLineItem { get; set; }

    public int CalendarYear { get; set; }
    public int CalendarMonth { get; set; }
    public decimal Amount { get; set; }
}
