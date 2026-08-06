namespace PanasonicPrototype.Api.Dtos;

public record StoreDashboardDto(
    int StoreId,
    string StoreName,
    int Year,
    int Month,
    decimal Budget,
    decimal ApprovedExpense,
    decimal PendingExpense,
    decimal Earnings,
    decimal ForecastedRevenue,
    decimal ForecastedExpense,
    decimal NetProfit,
    decimal BudgetVariance,
    int PendingApprovalsCount,
    decimal TargetProfitAmount,
    bool MetProfitTarget,
    decimal MarginPercent,
    decimal ProjectedRevenue,
    decimal ProjectedExpense,
    string PaceStatus);

public record OverviewDto(
    int Year,
    int Month,
    decimal TotalBudget,
    decimal TotalApprovedExpense,
    decimal TotalEarnings,
    decimal TotalNetProfit,
    int TotalPendingApprovals,
    decimal AggregateMarginPercent,
    int StoresMeetingTarget,
    int StoresTotal,
    List<StoreDashboardDto> Stores);

public record MonthlyTrendDto(int Year, int Month, decimal Budget, decimal ApprovedExpense, decimal Earnings, decimal ForecastedExpense);

public record CategoryBreakdownDto(string CategoryName, decimal Amount);

public record CategoryMarginDto(string CategoryName, decimal Revenue, decimal CostOfSales, decimal MarginPercent);
