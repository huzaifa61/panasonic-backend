using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

public class BudgetApprovalService
{
    private readonly AppDbContext _db;

    public BudgetApprovalService(AppDbContext db)
    {
        _db = db;
    }

    // Expenses already Approved or awaiting a decision (PendingLevel1/2) count against
    // the budget; Rejected expenses do not. This prevents a burst of concurrent
    // over-budget requests from all appearing "under budget" against the same budget line.
    public async Task<decimal> GetCommittedSpendAsync(int storeId, int year, int month, int? excludeExpenseId = null)
    {
        var query = _db.Expenses.Where(e =>
            e.StoreId == storeId &&
            e.Date.Year == year &&
            e.Date.Month == month &&
            e.Status != ExpenseStatus.Rejected);

        if (excludeExpenseId.HasValue)
        {
            query = query.Where(e => e.Id != excludeExpenseId.Value);
        }

        // SQLite's EF Core provider can't translate Sum() over decimal server-side,
        // so aggregate client-side after materializing the (small) result set.
        var amounts = await query.Select(e => e.Amount).ToListAsync();
        return amounts.Sum();
    }

    // Actual vs. planned overage at the line-item level (generalizes the store-level
    // budget check to a specific budget line's forecast for that month).
    public async Task<decimal> GetLineItemCommittedSpendAsync(int lineItemId, int year, int month, int? excludeExpenseId = null)
    {
        var query = _db.Expenses.Where(e =>
            e.BudgetLineItemId == lineItemId &&
            e.Date.Year == year &&
            e.Date.Month == month &&
            e.Status != ExpenseStatus.Rejected);

        if (excludeExpenseId.HasValue)
        {
            query = query.Where(e => e.Id != excludeExpenseId.Value);
        }

        var amounts = await query.Select(e => e.Amount).ToListAsync();
        return amounts.Sum();
    }

    public async Task<(Expense Expense, string? ApprovalReason)> CreateExpenseAsync(
        int storeId, int categoryId, string description, decimal amount, DateOnly date, int? lineItemId = null)
    {
        var store = await _db.Stores.FindAsync(storeId)
            ?? throw new ArgumentException($"Store {storeId} not found.");

        var budget = await _db.Budgets.FirstOrDefaultAsync(b =>
            b.StoreId == storeId && b.Year == date.Year && b.Month == date.Month);
        var budgetAmount = budget?.Amount ?? 0m;

        var committedSoFar = await GetCommittedSpendAsync(storeId, date.Year, date.Month);
        var projectedTotal = committedSoFar + amount;
        var exceedsBudget = projectedTotal > budgetAmount;

        // A single expense above the Regional Manager's configured threshold also
        // requires approval, even when the store is still within its monthly budget.
        var approvalSettings = await _db.ApprovalSettings.FirstOrDefaultAsync();
        var threshold = approvalSettings?.ExpenseApprovalThreshold ?? 0m;
        var exceedsThreshold = threshold > 0m && amount > threshold;

        var lineItem = lineItemId.HasValue ? await _db.BudgetLineItems.FindAsync(lineItemId.Value) : null;
        var lineItemForecastAmount = 0m;
        var lineItemCommittedSoFar = 0m;
        var exceedsLineItemForecast = false;

        if (lineItem != null)
        {
            var fiscalYear = FiscalCalendarHelper.GetFiscalYear(date.Year, date.Month);
            var currentVersion = await _db.ForecastVersions
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.StoreId == storeId && v.FiscalYear == fiscalYear && v.IsCurrent);
            var forecastLine = currentVersion?.Lines.FirstOrDefault(l =>
                l.BudgetLineItemId == lineItem.Id && l.CalendarYear == date.Year && l.CalendarMonth == date.Month);

            lineItemForecastAmount = forecastLine?.Amount ?? await _db.BudgetLineItemMonths
                .Where(m => m.BudgetLineItemId == lineItem.Id && m.CalendarYear == date.Year && m.CalendarMonth == date.Month)
                .Select(m => m.Amount)
                .FirstOrDefaultAsync();

            lineItemCommittedSoFar = await GetLineItemCommittedSpendAsync(lineItem.Id, date.Year, date.Month);
            exceedsLineItemForecast = (lineItemCommittedSoFar + amount) > lineItemForecastAmount;
        }

        var requiresApproval = exceedsBudget || exceedsThreshold || exceedsLineItemForecast;

        var reasons = new List<string>();
        if (exceedsBudget)
        {
            reasons.Add(committedSoFar > budgetAmount
                ? $"This store's expenses for the month were already over its {budgetAmount:N2} budget before this expense " +
                  $"(existing approved + pending total: {committedSoFar:N2}), so any additional expense needs approval."
                : $"This expense brings the store's total submitted expenses for the month to {projectedTotal:N2}, " +
                  $"which exceeds its {budgetAmount:N2} budget.");
        }
        if (exceedsThreshold)
        {
            reasons.Add($"This expense ({amount:N2}) exceeds the {threshold:N2} single-expense approval threshold set by the Regional Manager.");
        }
        if (exceedsLineItemForecast && lineItem != null)
        {
            reasons.Add($"This expense pushes budget line \"{lineItem.GLCode} - {lineItem.Description}\" to " +
                $"{lineItemCommittedSoFar + amount:N2} for this month, exceeding its planned {lineItemForecastAmount:N2}.");
        }
        var approvalReason = reasons.Count > 0 ? string.Join(" ", reasons) : null;

        var expense = new Expense
        {
            StoreId = storeId,
            CategoryId = categoryId,
            BudgetLineItemId = lineItemId,
            Description = description,
            Amount = amount,
            Date = date,
            CreatedAt = DateTime.UtcNow,
            Status = requiresApproval ? ExpenseStatus.PendingLevel1Approval : ExpenseStatus.Approved
        };

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync();

        if (requiresApproval)
        {
            _db.Approvals.Add(new Approval
            {
                ExpenseId = expense.Id,
                Level = 1,
                ApproverRole = "Store Manager",
                ApproverName = store.StoreManagerName,
                Decision = ApprovalDecision.Pending
            });
            _db.Approvals.Add(new Approval
            {
                ExpenseId = expense.Id,
                Level = 2,
                ApproverRole = "Regional Manager",
                ApproverName = store.RegionalManagerName,
                Decision = ApprovalDecision.Pending
            });
            await _db.SaveChangesAsync();
        }

        return (expense, approvalReason);
    }

    public async Task<Approval> DecideApprovalAsync(int approvalId, bool approve, string? comments)
    {
        var approval = await _db.Approvals
            .Include(a => a.Expense)
            .FirstOrDefaultAsync(a => a.Id == approvalId)
            ?? throw new ArgumentException($"Approval {approvalId} not found.");

        if (approval.Decision != ApprovalDecision.Pending)
        {
            throw new InvalidOperationException("This approval has already been decided.");
        }

        var expense = approval.Expense!;

        if (approval.Level == 2 && expense.Status != ExpenseStatus.PendingLevel2Approval)
        {
            throw new InvalidOperationException("Level 1 approval must be completed before Level 2 can act.");
        }
        if (approval.Level == 1 && expense.Status != ExpenseStatus.PendingLevel1Approval)
        {
            throw new InvalidOperationException("This expense is not awaiting Level 1 approval.");
        }

        approval.Decision = approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        approval.DecidedAt = DateTime.UtcNow;
        approval.Comments = comments;

        if (!approve)
        {
            expense.Status = ExpenseStatus.Rejected;
        }
        else if (approval.Level == 1)
        {
            expense.Status = ExpenseStatus.PendingLevel2Approval;
        }
        else
        {
            expense.Status = ExpenseStatus.Approved;
        }

        await _db.SaveChangesAsync();
        return approval;
    }
}
