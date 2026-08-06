namespace PanasonicPrototype.Api.Dtos;

public record GlobalPlanningMonthCellDto(
    int CalendarYear,
    int CalendarMonth,
    decimal Budget,
    bool IsBudgetEditable,
    decimal ForecastedExpense,
    bool IsForecastEditable,
    bool IsMonthLocked);

public record GlobalPlanningStoreDto(
    int StoreId,
    string StoreName,
    List<GlobalPlanningMonthCellDto> Months);

public record GlobalPlanningCellSaveDto(
    int StoreId,
    int CalendarYear,
    int CalendarMonth,
    decimal? Budget,
    decimal? ForecastedExpense);

public record SaveGlobalPlanningRequest(List<GlobalPlanningCellSaveDto> Cells);
