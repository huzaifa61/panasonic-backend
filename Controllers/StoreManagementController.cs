using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Controllers;

[Authorize(Roles = "RegionalManager")]
[ApiController]
[Route("api/store-management")]
public class StoreManagementController : ControllerBase
{
    private readonly AppDbContext _db;

    public StoreManagementController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPut("{storeId}/budget")]
    public async Task<IActionResult> UpsertBudget(int storeId, [FromBody] UpsertBudgetRequest request)
    {
        if (!await _db.Stores.AnyAsync(s => s.Id == storeId))
        {
            return NotFound(new { error = $"Store {storeId} not found." });
        }

        // Once a store has line-item budgets for the fiscal year covering this calendar
        // month, its Budget.Amount is a derived rollup (see BudgetPlanningService) - direct
        // edits here would conflict with that, so they're blocked for those months.
        var fiscalYear = Services.FiscalCalendarHelper.GetFiscalYear(request.Year, request.Month);
        var hasLineItems = await _db.BudgetLineItems.AnyAsync(li => li.StoreId == storeId && li.FiscalYear == fiscalYear);
        if (hasLineItems)
        {
            return Conflict(new { error = "This store's budget for this period is managed via Budget Planning line items and can't be edited directly here." });
        }

        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.StoreId == storeId && b.Year == request.Year && b.Month == request.Month);
        if (budget == null)
        {
            budget = new Budget { StoreId = storeId, Year = request.Year, Month = request.Month, Amount = request.Amount };
            _db.Budgets.Add(budget);
        }
        else
        {
            budget.Amount = request.Amount;
        }

        await _db.SaveChangesAsync();
        return Ok(new { budget.StoreId, budget.Year, budget.Month, budget.Amount });
    }

    // The store's total approved budget envelope for a fiscal year - an independent
    // ceiling (unlike Budget, which Budget Planning auto-syncs) that the sum of all
    // GL-code line items' Approved Amount is validated against.
    [HttpGet("{storeId}/allocated-budget")]
    public async Task<IActionResult> GetAllocatedBudget(int storeId, [FromQuery] int fiscalYear)
    {
        var allocated = await _db.StoreAllocatedBudgets
            .FirstOrDefaultAsync(a => a.StoreId == storeId && a.FiscalYear == fiscalYear);
        if (allocated == null)
        {
            return Ok(new StoreAllocatedBudgetDto(storeId, fiscalYear, 0m));
        }
        return Ok(new StoreAllocatedBudgetDto(allocated.StoreId, allocated.FiscalYear, allocated.TotalAllocatedBudget));
    }

    [HttpPut("{storeId}/allocated-budget")]
    public async Task<IActionResult> UpsertAllocatedBudget(int storeId, [FromBody] UpsertStoreAllocatedBudgetRequest request)
    {
        if (!await _db.Stores.AnyAsync(s => s.Id == storeId))
        {
            return NotFound(new { error = $"Store {storeId} not found." });
        }

        var allocated = await _db.StoreAllocatedBudgets
            .FirstOrDefaultAsync(a => a.StoreId == storeId && a.FiscalYear == request.FiscalYear);
        if (allocated == null)
        {
            allocated = new StoreAllocatedBudget { StoreId = storeId, FiscalYear = request.FiscalYear, TotalAllocatedBudget = request.TotalAllocatedBudget };
            _db.StoreAllocatedBudgets.Add(allocated);
        }
        else
        {
            allocated.TotalAllocatedBudget = request.TotalAllocatedBudget;
        }

        await _db.SaveChangesAsync();
        return Ok(new StoreAllocatedBudgetDto(allocated.StoreId, allocated.FiscalYear, allocated.TotalAllocatedBudget));
    }

    [HttpPut("{storeId}/target")]
    public async Task<IActionResult> UpsertTarget(int storeId, [FromBody] UpsertTargetRequest request)
    {
        if (!await _db.Stores.AnyAsync(s => s.Id == storeId))
        {
            return NotFound(new { error = $"Store {storeId} not found." });
        }

        var forecast = await _db.Forecasts.FirstOrDefaultAsync(f => f.StoreId == storeId && f.Year == request.Year && f.Month == request.Month);
        if (forecast == null)
        {
            forecast = new Forecast
            {
                StoreId = storeId,
                Year = request.Year,
                Month = request.Month,
                ForecastedRevenue = request.ForecastedRevenue ?? 0m,
                ForecastedExpense = request.ForecastedExpense ?? 0m,
                TargetProfitAmount = request.TargetProfitAmount
            };
            _db.Forecasts.Add(forecast);
        }
        else
        {
            forecast.TargetProfitAmount = request.TargetProfitAmount;
            if (request.ForecastedRevenue.HasValue) forecast.ForecastedRevenue = request.ForecastedRevenue.Value;
            if (request.ForecastedExpense.HasValue) forecast.ForecastedExpense = request.ForecastedExpense.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(new
        {
            forecast.StoreId,
            forecast.Year,
            forecast.Month,
            forecast.TargetProfitAmount,
            forecast.ForecastedRevenue,
            forecast.ForecastedExpense
        });
    }

    [HttpPut("{storeId}/headcount")]
    public async Task<IActionResult> UpsertHeadcount(int storeId, [FromBody] UpsertHeadcountRequest request)
    {
        if (!await _db.Stores.AnyAsync(s => s.Id == storeId))
        {
            return NotFound(new { error = $"Store {storeId} not found." });
        }

        var headcount = await _db.Headcounts.FirstOrDefaultAsync(h => h.StoreId == storeId && h.Year == request.Year && h.Month == request.Month);
        if (headcount == null)
        {
            headcount = new Headcount
            {
                StoreId = storeId,
                Year = request.Year,
                Month = request.Month,
                CurrentHeadcount = request.CurrentHeadcount,
                ForecastedHeadcountNeed = request.ForecastedHeadcountNeed
            };
            _db.Headcounts.Add(headcount);
        }
        else
        {
            headcount.CurrentHeadcount = request.CurrentHeadcount;
            headcount.ForecastedHeadcountNeed = request.ForecastedHeadcountNeed;
        }

        await _db.SaveChangesAsync();
        return Ok(new
        {
            headcount.StoreId,
            headcount.Year,
            headcount.Month,
            headcount.CurrentHeadcount,
            headcount.ForecastedHeadcountNeed,
            Gap = headcount.ForecastedHeadcountNeed - headcount.CurrentHeadcount
        });
    }
}
