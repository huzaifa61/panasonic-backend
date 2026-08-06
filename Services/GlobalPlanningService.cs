using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

// A cross-store view of Store Management's per-month Budget and Forecasted
// Expense, letting a Regional Manager set them across the whole fiscal year
// (Apr-Mar) for one store or every store at once, instead of one month/store
// at a time via Store Management. Both Budget and Forecasted Expense are
// read-only wherever Budget Planning line items already govern that
// store/fiscal-year (mirroring Budget Planning's own live totals) - only
// stores with no line items at all can have either edited directly here.
// Both respect the same past-month lock Budget Planning uses.
public class GlobalPlanningService
{
    private readonly AppDbContext _db;

    public GlobalPlanningService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<GlobalPlanningStoreDto>> GetGridAsync(int fiscalYear, DateOnly asOf, int? storeId)
    {
        var stores = await _db.Stores
            .Where(s => storeId == null || s.Id == storeId)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var fiscalMonths = FiscalCalendarHelper.GetFiscalYearMonths(fiscalYear);
        var storeIds = stores.Select(s => s.Id).ToList();

        // Budget Planning line items are the true source of Budget for any
        // store/fiscal-year that has them. The Budgets table is only a lazily
        // updated mirror - it's synced from line-item totals inside
        // BudgetPlanningService.SaveBudgetCellsAsync, which fires only when a
        // user explicitly clicks "Save Budget" on that grid. Line items
        // created any other way (e.g. seeded, or created via CreateLineItemAsync
        // without a subsequent save) never touch it, so reading Budgets
        // directly here can show stale/zero figures that don't match what
        // Budget Planning actually has. Compute Budget live from line items
        // instead, whenever the store has any for this fiscal year.
        var lineItemMonths = await _db.BudgetLineItems
            .Where(li => storeIds.Contains(li.StoreId) && li.FiscalYear == fiscalYear)
            .Include(li => li.Months)
            .ToListAsync();

        var storesWithLineItems = lineItemMonths.Select(li => li.StoreId).Distinct().ToHashSet();

        var lineItemBudgetLookup = new Dictionary<(int StoreId, int Year, int Month), decimal>();
        foreach (var li in lineItemMonths)
        {
            foreach (var m in li.Months)
            {
                var key = (li.StoreId, m.CalendarYear, m.CalendarMonth);
                lineItemBudgetLookup[key] = lineItemBudgetLookup.GetValueOrDefault(key) + m.Amount;
            }
        }

        var budgets = await _db.Budgets.Where(b => storeIds.Contains(b.StoreId)).ToListAsync();
        var forecasts = await _db.Forecasts.Where(f => storeIds.Contains(f.StoreId)).ToListAsync();

        // Same story for Forecasted Expense: Budget Planning's own "Total
        // Forecast" row is the sum, per month, of each line item's
        // ForecastVersionLine amount (falling back to that line item's Budget
        // when no forecast has been proposed for it yet) - see
        // BudgetPlanningService.GetGridAsync. The Forecasts table here is a
        // separate, independently-editable number with no relation to that.
        // Mirror the same live computation for stores with line items so the
        // two pages agree, and only leave Forecasted Expense directly
        // editable here for stores with no line items at all.
        var forecastVersions = await _db.ForecastVersions
            .Where(v => storeIds.Contains(v.StoreId) && v.FiscalYear == fiscalYear)
            .Include(v => v.Lines)
            .ToListAsync();

        var result = new List<GlobalPlanningStoreDto>();
        foreach (var store in stores)
        {
            var hasLineItems = storesWithLineItems.Contains(store.Id);
            var storeLineItems = lineItemMonths.Where(li => li.StoreId == store.Id).ToList();
            var storeVersions = forecastVersions.Where(v => v.StoreId == store.Id).ToList();
            var currentVersion = storeVersions.FirstOrDefault(v => v.IsCurrent);
            var months = new List<GlobalPlanningMonthCellDto>();

            foreach (var (cy, cm) in fiscalMonths)
            {
                var isMonthLocked = IsBefore(cy, cm, asOf.Year, asOf.Month);
                var budget = hasLineItems
                    ? lineItemBudgetLookup.GetValueOrDefault((store.Id, cy, cm))
                    : budgets.FirstOrDefault(b => b.StoreId == store.Id && b.Year == cy && b.Month == cm)?.Amount ?? 0m;

                decimal forecast;
                if (hasLineItems)
                {
                    var effectiveVersion = isMonthLocked
                        ? storeVersions
                            .Where(v => !IsBefore(cy, cm, v.AsOfCalendarYear, v.AsOfCalendarMonth))
                            .OrderByDescending(v => v.AsOfCalendarYear)
                            .ThenByDescending(v => v.AsOfCalendarMonth)
                            .FirstOrDefault()
                        : currentVersion;

                    forecast = storeLineItems.Sum(li =>
                        effectiveVersion?.Lines.FirstOrDefault(l => l.BudgetLineItemId == li.Id && l.CalendarYear == cy && l.CalendarMonth == cm)?.Amount
                        ?? li.Months.FirstOrDefault(m => m.CalendarYear == cy && m.CalendarMonth == cm)?.Amount
                        ?? 0m);
                }
                else
                {
                    forecast = forecasts.FirstOrDefault(f => f.StoreId == store.Id && f.Year == cy && f.Month == cm)?.ForecastedExpense ?? 0m;
                }

                months.Add(new GlobalPlanningMonthCellDto(
                    cy, cm, budget, !hasLineItems && !isMonthLocked, forecast, !hasLineItems && !isMonthLocked, isMonthLocked));
            }

            result.Add(new GlobalPlanningStoreDto(store.Id, store.Name, months));
        }

        return result;
    }

    public async Task SaveCellsAsync(List<GlobalPlanningCellSaveDto> cells, DateOnly asOf)
    {
        if (cells.Count == 0) return;

        var storeIds = cells.Select(c => c.StoreId).Distinct().ToList();
        var fiscalYears = cells.Select(c => FiscalCalendarHelper.GetFiscalYear(c.CalendarYear, c.CalendarMonth)).Distinct().ToList();

        var storesWithLineItems = (await _db.BudgetLineItems
                .Where(li => storeIds.Contains(li.StoreId) && fiscalYears.Contains(li.FiscalYear))
                .Select(li => li.StoreId)
                .Distinct()
                .ToListAsync())
            .ToHashSet();

        foreach (var cell in cells)
        {
            if (IsBefore(cell.CalendarYear, cell.CalendarMonth, asOf.Year, asOf.Month))
            {
                throw new InvalidOperationException(
                    $"{cell.CalendarYear}-{cell.CalendarMonth:00} has already passed and can't be edited.");
            }

            if (cell.Budget.HasValue)
            {
                if (storesWithLineItems.Contains(cell.StoreId))
                {
                    throw new InvalidOperationException(
                        "Budget for this store/fiscal year is managed via Budget Planning line items and can't be edited here.");
                }

                var budget = await _db.Budgets.FirstOrDefaultAsync(b =>
                    b.StoreId == cell.StoreId && b.Year == cell.CalendarYear && b.Month == cell.CalendarMonth);
                if (budget == null)
                {
                    _db.Budgets.Add(new Budget { StoreId = cell.StoreId, Year = cell.CalendarYear, Month = cell.CalendarMonth, Amount = cell.Budget.Value });
                }
                else
                {
                    budget.Amount = cell.Budget.Value;
                }
            }

            if (cell.ForecastedExpense.HasValue)
            {
                if (storesWithLineItems.Contains(cell.StoreId))
                {
                    throw new InvalidOperationException(
                        "Forecasted Expense for this store/fiscal year is managed via Budget Planning's line-item forecast and can't be edited here.");
                }

                var forecast = await _db.Forecasts.FirstOrDefaultAsync(f =>
                    f.StoreId == cell.StoreId && f.Year == cell.CalendarYear && f.Month == cell.CalendarMonth);
                if (forecast == null)
                {
                    _db.Forecasts.Add(new Forecast
                    {
                        StoreId = cell.StoreId,
                        Year = cell.CalendarYear,
                        Month = cell.CalendarMonth,
                        ForecastedExpense = cell.ForecastedExpense.Value,
                        ForecastedRevenue = 0m,
                        TargetProfitAmount = 0m
                    });
                }
                else
                {
                    forecast.ForecastedExpense = cell.ForecastedExpense.Value;
                }
            }
        }

        await _db.SaveChangesAsync();
    }

    private static bool IsBefore(int year, int month, int otherYear, int otherMonth) =>
        year < otherYear || (year == otherYear && month < otherMonth);
}
