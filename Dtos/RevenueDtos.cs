namespace PanasonicPrototype.Api.Dtos;

public record CreateRevenueRequest(int StoreId, int CategoryId, decimal Amount, DateOnly Date, string? Description);

public record RevenueDto(int Id, int StoreId, string StoreName, int CategoryId, string CategoryName, decimal Amount, DateOnly Date, string? Description);

public record BulkRevenueRowRequest(string StoreName, string CategoryName, string? Description, decimal Amount, DateOnly Date);

public record BulkRevenueRowResult(int RowIndex, bool Success, string? Error, RevenueDto? Revenue);
