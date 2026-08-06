namespace PanasonicPrototype.Api.Models;

public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected
}

public class Approval
{
    public int Id { get; set; }
    public int ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    // 1 = Store Manager, 2 = Regional Manager
    public int Level { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public string? ApproverName { get; set; }

    public ApprovalDecision Decision { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comments { get; set; }
}
