namespace PanasonicPrototype.Api.Models;

public class Forecast
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }
    public decimal ForecastedRevenue { get; set; }
    public decimal ForecastedExpense { get; set; }
    public decimal TargetProfitAmount { get; set; }
}
