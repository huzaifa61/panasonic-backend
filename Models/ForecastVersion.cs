namespace PanasonicPrototype.Api.Models;

public class ForecastVersion
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int FiscalYear { get; set; }

    // The calendar month in which this snapshot was made/locked.
    public int AsOfCalendarYear { get; set; }
    public int AsOfCalendarMonth { get; set; }

    public string Label { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Exactly one true row per (StoreId, FiscalYear) - the mutable draft the grid edits.
    public bool IsCurrent { get; set; }

    public List<ForecastVersionLine> Lines { get; set; } = new();
}
