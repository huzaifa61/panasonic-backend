namespace PanasonicPrototype.Api.Dtos;

public record CreateExpenseRequest(int StoreId, int CategoryId, string Description, decimal Amount, DateOnly Date, int? LineItemId = null);

public record ApprovalDto(int Id, int Level, string ApproverRole, string? ApproverName, string Decision, DateTime? DecidedAt, string? Comments);

public record PendingApprovalDto(
    int Id,
    string Type, // "Expense" | "Discount" | "ForecastRevision"
    int Level,
    string ApproverRole,
    string? ApproverName,
    int? ExpenseId,
    int? SaleId,
    int StoreId,
    string StoreName,
    string? CategoryName,
    string? Description,
    decimal Amount,
    DateOnly Date,
    string? CustomerName,
    decimal? RequestedDiscountPercent,
    int? LineItemId = null,
    string? GLCode = null,
    decimal? PreviousAmount = null);

public record ExpenseDto(
    int Id,
    int StoreId,
    string StoreName,
    string CategoryName,
    string Description,
    decimal Amount,
    DateOnly Date,
    string Status,
    List<ApprovalDto> Approvals,
    int? LineItemId = null,
    string? GLCode = null,
    string? ApprovalReason = null);

public record DecideApprovalRequest(bool Approve, string? Comments);

public record BulkExpenseRowRequest(string StoreName, string CategoryName, string? LineItemGLCode, string Description, decimal Amount, DateOnly Date);

public record BulkExpenseRowResult(int RowIndex, bool Success, string? Error, ExpenseDto? Expense);
