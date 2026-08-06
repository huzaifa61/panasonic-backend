namespace PanasonicPrototype.Api.Dtos;

public record MonthCellDto(
    int CalendarYear,
    int CalendarMonth,
    decimal Budget,
    decimal Forecast,
    decimal Result,
    bool IsLocked,
    bool HasPendingForecastApproval,
    decimal? PendingForecastAmount);

public record LineItemGridRowDto(
    int LineItemId,
    string GLCode,
    string Description,
    string CostCenter,
    string ExpenseCategory,
    decimal Units,
    decimal Rate,
    DateOnly StartDate,
    DateOnly EndDate,
    int NoOfMonths,
    decimal TotalCost,
    decimal ApprovedAmount,
    List<MonthCellDto> Months);

public record CategoryRollupRowDto(
    string ExpenseCategory,
    List<decimal> MonthlyBudget,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4,
    decimal H1,
    decimal H2,
    decimal FiscalYearTotal);

public record StoreBudgetGridDto(
    int StoreId,
    int FiscalYear,
    int AsOfCalendarYear,
    int AsOfCalendarMonth,
    int? CurrentForecastVersionId,
    string? CurrentForecastLabel,
    List<LineItemGridRowDto> LineItems,
    List<CategoryRollupRowDto> CategoryRollup,
    List<decimal> GrandTotalMonthlyBudget,
    decimal GrandTotalFiscalYearBudget,
    List<decimal> GrandTotalMonthlyForecast,
    decimal GrandTotalFiscalYearForecast,
    List<decimal> GrandTotalMonthlyActual,
    decimal GrandTotalFiscalYearActual,
    decimal? TotalAllocatedBudget);

public record StoreAllocatedBudgetDto(int StoreId, int FiscalYear, decimal TotalAllocatedBudget);

public record UpsertStoreAllocatedBudgetRequest(int FiscalYear, decimal TotalAllocatedBudget);

public record UpsertLineItemRequest(
    string GLCode,
    string Description,
    string CostCenter,
    string ExpenseCategory,
    decimal Units,
    decimal Rate,
    DateOnly StartDate,
    DateOnly EndDate,
    int NoOfMonths,
    decimal ApprovedAmount);

public record BudgetCellSaveDto(int LineItemId, int CalendarYear, int CalendarMonth, decimal Amount);

public record SaveBudgetGridRequest(List<BudgetCellSaveDto> Cells);

public record ProposeForecastRequest(List<BudgetCellSaveDto> Cells);

public record ForecastRevisionResultDto(
    List<BudgetCellSaveDto> AppliedImmediately,
    List<ForecastRevisionApprovalDto> PendingApprovals);

public record ForecastRevisionApprovalDto(
    int Id,
    int StoreId,
    int LineItemId,
    string GLCode,
    string Description,
    int CalendarYear,
    int CalendarMonth,
    decimal ProposedAmount,
    decimal PreviousAmount,
    int Level,
    string ApproverRole,
    string? ApproverName,
    string Decision,
    DateTime? DecidedAt,
    string? Comments,
    DateTime CreatedAt);

public record LockForecastRequest(string? Label);

public record ForecastVersionDto(
    int Id,
    string Label,
    int AsOfCalendarYear,
    int AsOfCalendarMonth,
    DateTime CreatedAt,
    bool IsCurrent);
