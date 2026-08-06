using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

public class DashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StoreDashboardDto> GetStoreDashboardAsync(int storeId, int year, int month, DateOnly? asOf = null)
    {
        var store = await _db.Stores.FindAsync(storeId)
            ?? throw new ArgumentException($"Store {storeId} not found.");

        return await BuildDashboardAsync(store, year, month, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task<OverviewDto> GetOverviewAsync(int year, int month, DateOnly? asOf = null, int? storeIdFilter = null)
    {
        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var storeQuery = _db.Stores.AsQueryable();
        if (storeIdFilter.HasValue)
        {
            storeQuery = storeQuery.Where(s => s.Id == storeIdFilter.Value);
        }
        var stores = await storeQuery.ToListAsync();
        var storeDashboards = new List<StoreDashboardDto>();
        foreach (var store in stores)
        {
            storeDashboards.Add(await BuildDashboardAsync(store, year, month, effectiveAsOf));
        }

        var totalEarnings = storeDashboards.Sum(s => s.Earnings);
        var totalNetProfit = storeDashboards.Sum(s => s.NetProfit);

        return new OverviewDto(
            year,
            month,
            storeDashboards.Sum(s => s.Budget),
            storeDashboards.Sum(s => s.ApprovedExpense),
            totalEarnings,
            totalNetProfit,
            storeDashboards.Sum(s => s.PendingApprovalsCount),
            totalEarnings == 0m ? 0m : totalNetProfit / totalEarnings * 100m,
            storeDashboards.Count(s => s.MetProfitTarget),
            storeDashboards.Count,
            storeDashboards);
    }

    // Returns up to monthsBack months ending at (endYear, endMonth) inclusive, oldest first.
    // Each historical month is treated as fully elapsed (pace fields are not meaningful for closed periods).
    public async Task<List<MonthlyTrendDto>> GetTrendAsync(int? storeId, int endYear, int endMonth, int monthsBack)
    {
        var stores = storeId.HasValue
            ? new List<Store> { await _db.Stores.FindAsync(storeId.Value) ?? throw new ArgumentException($"Store {storeId} not found.") }
            : await _db.Stores.ToListAsync();

        var result = new List<MonthlyTrendDto>();
        var cursor = new DateOnly(endYear, endMonth, 1);

        for (var i = monthsBack - 1; i >= 0; i--)
        {
            var target = cursor.AddMonths(-i);
            var monthEnd = new DateOnly(target.Year, target.Month, DateTime.DaysInMonth(target.Year, target.Month));
            decimal budget = 0m, approvedExpense = 0m, earnings = 0m, forecastedExpense = 0m;

            foreach (var store in stores)
            {
                var dashboard = await BuildDashboardAsync(store, target.Year, target.Month, monthEnd);
                budget += dashboard.Budget;
                approvedExpense += dashboard.ApprovedExpense;
                earnings += dashboard.Earnings;
                forecastedExpense += dashboard.ForecastedExpense;
            }

            result.Add(new MonthlyTrendDto(target.Year, target.Month, budget, approvedExpense, earnings, forecastedExpense));
        }

        return result;
    }

    public async Task<List<CategoryBreakdownDto>> GetCategoryBreakdownAsync(int? storeId, int year, int month)
    {
        var query = _db.Expenses
            .Include(e => e.Category)
            .Where(e => e.Date.Year == year && e.Date.Month == month && e.Status == ExpenseStatus.Approved);

        if (storeId.HasValue)
        {
            query = query.Where(e => e.StoreId == storeId.Value);
        }

        var expenses = await query.ToListAsync();

        return expenses
            .GroupBy(e => e.Category?.Name ?? "Uncategorized")
            .Select(g => new CategoryBreakdownDto(g.Key, g.Sum(e => e.Amount)))
            .OrderByDescending(c => c.Amount)
            .ToList();
    }

    // Margin per product category, computed purely from Revenue rows (Amount vs.
    // CostOfSales). Deliberately not netted against the Expense table: those are
    // operating costs (staff, marketing, repairs) against a monthly opex budget,
    // not a per-product cost basis, and mixing the two would double-count against
    // NetProfit/budget variance elsewhere on the dashboard.
    public async Task<List<CategoryMarginDto>> GetCategoryMarginAsync(int? storeId, int year, int month)
    {
        var revenueQuery = _db.Revenues
            .Include(r => r.Category)
            .Where(r => r.Date.Year == year && r.Date.Month == month);

        if (storeId.HasValue)
        {
            revenueQuery = revenueQuery.Where(r => r.StoreId == storeId.Value);
        }

        var revenues = await revenueQuery.ToListAsync();

        return revenues
            .Where(r => r.Category != null)
            .GroupBy(r => r.Category!.Name)
            .Select(g =>
            {
                var revenue = g.Sum(r => r.Amount);
                var costOfSales = g.Sum(r => r.CostOfSales);
                var marginPercent = revenue == 0m ? 0m : (revenue - costOfSales) / revenue * 100m;
                return new CategoryMarginDto(g.Key, revenue, costOfSales, marginPercent);
            })
            .OrderByDescending(c => c.Revenue)
            .ToList();
    }

    private async Task<StoreDashboardDto> BuildDashboardAsync(Store store, int year, int month, DateOnly asOf)
    {
        var budget = await _db.Budgets
            .Where(b => b.StoreId == store.Id && b.Year == year && b.Month == month)
            .Select(b => b.Amount)
            .FirstOrDefaultAsync();

        var expenses = await _db.Expenses
            .Where(e => e.StoreId == store.Id && e.Date.Year == year && e.Date.Month == month)
            .ToListAsync();

        var approvedExpense = expenses.Where(e => e.Status == ExpenseStatus.Approved).Sum(e => e.Amount);
        var pendingExpense = expenses.Where(e =>
            e.Status is ExpenseStatus.PendingLevel1Approval or ExpenseStatus.PendingLevel2Approval).Sum(e => e.Amount);

        var pendingApprovalsCount = expenses.Count(e =>
            e.Status is ExpenseStatus.PendingLevel1Approval or ExpenseStatus.PendingLevel2Approval);

        var revenueAmounts = await _db.Revenues
            .Where(r => r.StoreId == store.Id && r.Date.Year == year && r.Date.Month == month)
            .Select(r => r.Amount)
            .ToListAsync();
        var earnings = revenueAmounts.Sum();

        var forecast = await _db.Forecasts
            .FirstOrDefaultAsync(f => f.StoreId == store.Id && f.Year == year && f.Month == month);

        var netProfit = earnings - approvedExpense;
        var budgetVariance = budget - approvedExpense;

        var targetProfitAmount = forecast?.TargetProfitAmount ?? 0m;
        var metProfitTarget = netProfit >= targetProfitAmount;
        var marginPercent = earnings == 0m ? 0m : netProfit / earnings * 100m;

        var (projectedRevenue, projectedExpense, paceStatus) = ComputePace(
            year, month, asOf, earnings, approvedExpense, forecast?.ForecastedRevenue ?? 0m, forecast?.ForecastedExpense ?? 0m);

        return new StoreDashboardDto(
            store.Id,
            store.Name,
            year,
            month,
            budget,
            approvedExpense,
            pendingExpense,
            earnings,
            forecast?.ForecastedRevenue ?? 0m,
            forecast?.ForecastedExpense ?? 0m,
            netProfit,
            budgetVariance,
            pendingApprovalsCount,
            targetProfitAmount,
            metProfitTarget,
            marginPercent,
            projectedRevenue,
            projectedExpense,
            paceStatus);
    }

    // Projects month-end revenue/expense from the current run-rate (actual-to-date / elapsed fraction of
    // the month) and compares against the forecast. A month that hasn't started yet, or that has no
    // forecast to compare against, reports "NotStarted"/"OnTrack" rather than dividing by zero.
    private static (decimal ProjectedRevenue, decimal ProjectedExpense, string PaceStatus) ComputePace(
        int year, int month, DateOnly asOf, decimal earnings, decimal approvedExpense,
        decimal forecastedRevenue, decimal forecastedExpense)
    {
        var monthStart = new DateOnly(year, month, 1);
        var asOfMonthStart = new DateOnly(asOf.Year, asOf.Month, 1);

        decimal elapsedFraction;
        if (monthStart < asOfMonthStart)
        {
            elapsedFraction = 1.0m; // a closed, past month
        }
        else if (monthStart > asOfMonthStart)
        {
            elapsedFraction = 0.0m; // month hasn't started yet
        }
        else
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            elapsedFraction = Math.Min(1.0m, (decimal)asOf.Day / daysInMonth);
        }

        if (elapsedFraction <= 0m)
        {
            return (0m, 0m, "NotStarted");
        }

        var projectedRevenue = earnings / elapsedFraction;
        var projectedExpense = approvedExpense / elapsedFraction;

        var revenueOnTrack = forecastedRevenue == 0m || projectedRevenue >= 0.95m * forecastedRevenue;
        var expenseOverPace = forecastedExpense > 0m && projectedExpense > forecastedExpense;

        var paceStatus = !revenueOnTrack ? "BehindPace" : expenseOverPace ? "OverPace" : "OnTrack";

        return (projectedRevenue, projectedExpense, paceStatus);
    }
}
