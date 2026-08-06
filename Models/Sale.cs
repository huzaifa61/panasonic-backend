namespace PanasonicPrototype.Api.Models;

public enum SaleStatus
{
    AutoApproved,
    PendingLevel1Approval,
    PendingLevel2Approval,
    Approved,
    Rejected
}

public class Sale
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public decimal SaleAmount { get; set; }
    public decimal RequestedDiscountPercent { get; set; }
    public DateOnly Date { get; set; }
    public SaleStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<DiscountApproval> Approvals { get; set; } = new();
}
