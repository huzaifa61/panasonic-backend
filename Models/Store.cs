namespace PanasonicPrototype.Api.Models;

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string StoreManagerName { get; set; } = string.Empty;
    public string RegionalManagerName { get; set; } = string.Empty;

    public List<Budget> Budgets { get; set; } = new();
    public List<Expense> Expenses { get; set; } = new();
    public List<Revenue> Revenues { get; set; } = new();
    public List<Forecast> Forecasts { get; set; } = new();
}
