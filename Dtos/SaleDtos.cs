namespace PanasonicPrototype.Api.Dtos;

public record CreateSaleRequest(int StoreId, string CustomerName, decimal SaleAmount, decimal RequestedDiscountPercent, DateOnly Date);

public record SaleDto(
    int Id,
    int StoreId,
    string StoreName,
    string CustomerName,
    decimal SaleAmount,
    decimal RequestedDiscountPercent,
    DateOnly Date,
    string Status);
