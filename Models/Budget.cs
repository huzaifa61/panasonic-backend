namespace PanasonicPrototype.Api.Models;

public class Budget
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
}
