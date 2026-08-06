using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

public class ReportExportService
{
    private readonly AppDbContext _db;
    private readonly DashboardService _dashboardService;

    public ReportExportService(AppDbContext db, DashboardService dashboardService)
    {
        _db = db;
        _dashboardService = dashboardService;
    }

    public async Task<string> BuildStoreReportCsvAsync(int storeId, int year, int month)
    {
        var store = await _db.Stores.FindAsync(storeId)
            ?? throw new ArgumentException($"Store {storeId} not found.");
        var dashboard = await _dashboardService.GetStoreDashboardAsync(storeId, year, month);

        var sb = new StringBuilder();

        sb.AppendLine("Panasonic Store Budget Report");
        sb.AppendLine($"Store,{Csv(store.Name)}");
        sb.AppendLine($"Region,{Csv(store.RegionName)}");
        sb.AppendLine($"Period,{year}-{month:D2}");
        sb.AppendLine();

        sb.AppendLine("Summary");
        sb.AppendLine("Metric,Amount");
        sb.AppendLine($"Budget,{dashboard.Budget}");
        sb.AppendLine($"Approved Expense,{dashboard.ApprovedExpense}");
        sb.AppendLine($"Pending Expense (awaiting approval),{dashboard.PendingExpense}");
        sb.AppendLine($"Budget Variance,{dashboard.BudgetVariance}");
        sb.AppendLine($"Earnings (Revenue),{dashboard.Earnings}");
        sb.AppendLine($"Net Profit,{dashboard.NetProfit}");
        sb.AppendLine($"Forecasted Revenue,{dashboard.ForecastedRevenue}");
        sb.AppendLine($"Forecasted Expense,{dashboard.ForecastedExpense}");
        sb.AppendLine($"Pending Approvals Count,{dashboard.PendingApprovalsCount}");
        sb.AppendLine();

        sb.AppendLine("Expense Details");
        sb.AppendLine("Date,Category,Description,Amount,Status,Level1 Approver,Level1 Decision,Level2 Approver,Level2 Decision");

        var expenses = await _db.Expenses
            .Include(e => e.Category)
            .Include(e => e.Approvals)
            .Where(e => e.StoreId == storeId && e.Date.Year == year && e.Date.Month == month)
            .OrderBy(e => e.Date)
            .ToListAsync();

        foreach (var expense in expenses)
        {
            var level1 = expense.Approvals.FirstOrDefault(a => a.Level == 1);
            var level2 = expense.Approvals.FirstOrDefault(a => a.Level == 2);

            sb.AppendLine(string.Join(",",
                expense.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Csv(expense.Category?.Name ?? ""),
                Csv(expense.Description),
                expense.Amount.ToString(CultureInfo.InvariantCulture),
                expense.Status.ToString(),
                Csv(level1?.ApproverName ?? ""),
                level1?.Decision.ToString() ?? "",
                Csv(level2?.ApproverName ?? ""),
                level2?.Decision.ToString() ?? ""));
        }
        sb.AppendLine();

        sb.AppendLine("Revenue Details");
        sb.AppendLine("Date,Description,Amount");
        var revenues = await _db.Revenues
            .Where(r => r.StoreId == storeId && r.Date.Year == year && r.Date.Month == month)
            .OrderBy(r => r.Date)
            .ToListAsync();
        foreach (var revenue in revenues)
        {
            sb.AppendLine(string.Join(",",
                revenue.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Csv(revenue.Description ?? ""),
                revenue.Amount.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
