namespace PanasonicPrototype.Api.Dtos;

public record CreateStoreRequest(
    string Name,
    string City,
    string RegionName,
    string StoreManagerName,
    string RegionalManagerName,
    string ManagerUsername,
    string ManagerPassword);

public record UpdateStoreRequest(
    string Name,
    string City,
    string RegionName,
    string StoreManagerName,
    string RegionalManagerName);

public record UpsertBudgetRequest(int Year, int Month, decimal Amount);

public record UpsertTargetRequest(int Year, int Month, decimal TargetProfitAmount, decimal? ForecastedRevenue, decimal? ForecastedExpense);

public record UpsertHeadcountRequest(int Year, int Month, int CurrentHeadcount, int ForecastedHeadcountNeed);
