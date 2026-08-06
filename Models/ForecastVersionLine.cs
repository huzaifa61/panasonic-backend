namespace PanasonicPrototype.Api.Models;

public class ForecastVersionLine
{
    public int Id { get; set; }
    public int ForecastVersionId { get; set; }
    public ForecastVersion? ForecastVersion { get; set; }

    public int BudgetLineItemId { get; set; }
    public BudgetLineItem? BudgetLineItem { get; set; }

    public int CalendarYear { get; set; }
    public int CalendarMonth { get; set; }
    public decimal Amount { get; set; }
}
