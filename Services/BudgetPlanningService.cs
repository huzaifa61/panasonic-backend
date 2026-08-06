using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

public class BudgetPlanningService
{
    private readonly AppDbContext _db;

    public BudgetPlanningService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StoreBudgetGridDto> GetGridAsync(int storeId, int fiscalYear, DateOnly asOf)
    {
        var fiscalMonths = FiscalCalendarHelper.GetFiscalYearMonths(fiscalYear);

        var lineItems = await _db.BudgetLineItems
            .Include(li => li.Months)
            .Where(li => li.StoreId == storeId && li.FiscalYear == fiscalYear)
            .OrderBy(li => li.GLCode)
            .ToListAsync();

        var allocatedBudget = await _db.StoreAllocatedBudgets
            .FirstOrDefaultAsync(a => a.StoreId == storeId && a.FiscalYear == fiscalYear);

        // "Unallocated / Other" used to be synthesized purely for display, always
        // 0/locked, so there was nowhere to actually plan the gap between what's
        // allocated to the store and what's been assigned to real GL codes. Make
        // it a real line item instead (auto-created once, lazily, the first time
        // this store/fiscal-year is loaded after a ceiling exists) so it goes
        // through the exact same editable Plan/Forecast/locking machinery as any
        // other GL code, with its own cap set to the remaining allocated budget
        // at creation time - which then correctly reserves that room against the
        // cross-GL-code ceiling (EnsureWithinAllocatedBudgetAsync) like any other
        // line item's ApprovedAmount would.
        if (allocatedBudget != null && !lineItems.Any(li => li.GLCode == "OTHER"))
        {
            var remaining = allocatedBudget.TotalAllocatedBudget - lineItems.Sum(li => li.ApprovedAmount);
            var unallocatedLineItem = new BudgetLineItem
            {
                StoreId = storeId,
                FiscalYear = fiscalYear,
                GLCode = "OTHER",
                Description = "Unallocated / Other",
                CostCenter = "-",
                ExpenseCategory = "Operations Cost",
                Units = 0,
                Rate = 0,
                StartDate = new DateOnly(fiscalYear, 4, 1),
                EndDate = new DateOnly(fiscalYear + 1, 3, 31),
                NoOfMonths = 12,
                TotalCost = 0m,
                ApprovedAmount = Math.Max(remaining, 0m),
                CreatedAt = DateTime.UtcNow
            };
            foreach (var (cy, cm) in fiscalMonths)
            {
                unallocatedLineItem.Months.Add(new BudgetLineItemMonth { CalendarYear = cy, CalendarMonth = cm, Amount = 0m });
            }
            _db.BudgetLineItems.Add(unallocatedLineItem);
            await _db.SaveChangesAsync();
            lineItems.Add(unallocatedLineItem);
            lineItems = lineItems.OrderBy(li => li.GLCode).ToList();
        }

        var lineItemIds = lineItems.Select(li => li.Id).ToList();

        var currentVersion = await _db.ForecastVersions
            .Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.StoreId == storeId && v.FiscalYear == fiscalYear && v.IsCurrent);

        var allVersions = await _db.ForecastVersions
            .Include(v => v.Lines)
            .Where(v => v.StoreId == storeId && v.FiscalYear == fiscalYear)
            .ToListAsync();

        var expenseRows = await _db.Expenses
            .Where(e => e.BudgetLineItemId != null && lineItemIds.Contains(e.BudgetLineItemId!.Value) && e.Status == ExpenseStatus.Approved)
            .Select(e => new { LineItemId = e.BudgetLineItemId!.Value, e.Date, e.Amount })
            .ToListAsync();

        var resultLookup = expenseRows
            .GroupBy(e => (e.LineItemId, e.Date.Year, e.Date.Month))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // Approved expenses recorded without a specific Budget Line Item can't be
        // attributed to any one GL code, but they're still real store spend -
        // surface them as the "OTHER" row's Actual so the grid's totals reconcile
        // with the store's true approved-expense figure shown elsewhere
        // (Dashboard, Store Performance), instead of silently disappearing.
        var unallocatedRows = await _db.Expenses
            .Where(e => e.StoreId == storeId && e.BudgetLineItemId == null && e.Status == ExpenseStatus.Approved)
            .Select(e => new { e.Date, e.Amount })
            .ToListAsync();

        var unallocatedLookup = unallocatedRows
            .GroupBy(e => (e.Date.Year, e.Date.Month))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // A cell with a Forecast Revision awaiting Store Manager/Regional Manager
        // decision shouldn't look freely editable - the grid was otherwise silent
        // about this, so a user could keep proposing over an already-pending
        // change with no indication anything was in flight.
        var pendingForecastApprovals = await _db.ForecastRevisionApprovals
            .Where(a => lineItemIds.Contains(a.BudgetLineItemId) && a.Decision == ApprovalDecision.Pending)
            .ToListAsync();
        var pendingForecastLookup = pendingForecastApprovals
            .GroupBy(a => (a.BudgetLineItemId, a.CalendarYear, a.CalendarMonth))
            .ToDictionary(g => g.Key, g => g.First().ProposedAmount);

        var rows = new List<LineItemGridRowDto>();
        foreach (var li in lineItems)
        {
            var monthCells = new List<MonthCellDto>();
            foreach (var (cy, cm) in fiscalMonths)
            {
                var isLocked = IsBefore(cy, cm, asOf.Year, asOf.Month);
                var budget = li.Months.FirstOrDefault(m => m.CalendarYear == cy && m.CalendarMonth == cm)?.Amount ?? 0m;

                decimal forecast;
                if (isLocked)
                {
                    var effectiveVersion = allVersions
                        .Where(v => !IsBefore(cy, cm, v.AsOfCalendarYear, v.AsOfCalendarMonth))
                        .OrderByDescending(v => v.AsOfCalendarYear)
                        .ThenByDescending(v => v.AsOfCalendarMonth)
                        .FirstOrDefault();
                    forecast = effectiveVersion?.Lines
                        .FirstOrDefault(l => l.BudgetLineItemId == li.Id && l.CalendarYear == cy && l.CalendarMonth == cm)?.Amount ?? budget;
                }
                else
                {
                    forecast = currentVersion?.Lines
                        .FirstOrDefault(l => l.BudgetLineItemId == li.Id && l.CalendarYear == cy && l.CalendarMonth == cm)?.Amount ?? budget;
                }

                // The "OTHER" row's real line-item id never has expenses posted
                // against it directly - its Actual instead needs to keep showing
                // approved expenses recorded with no Budget Line Item at all, so
                // the grid's totals reconcile with the store's true approved-spend
                // figure shown elsewhere (Dashboard, Store Performance).
                var result = li.GLCode == "OTHER"
                    ? unallocatedLookup.TryGetValue((cy, cm), out var unallocatedAmt) ? unallocatedAmt : 0m
                    : resultLookup.TryGetValue((li.Id, cy, cm), out var amt) ? amt : 0m;
                var hasPendingApproval = pendingForecastLookup.TryGetValue((li.Id, cy, cm), out var pendingAmt);
                monthCells.Add(new MonthCellDto(cy, cm, budget, forecast, result, isLocked, hasPendingApproval, hasPendingApproval ? pendingAmt : null));
            }

            rows.Add(new LineItemGridRowDto(
                li.Id, li.GLCode, li.Description, li.CostCenter, li.ExpenseCategory,
                li.Units, li.Rate, li.StartDate, li.EndDate, li.NoOfMonths,
                li.TotalCost, li.ApprovedAmount, monthCells));
        }

        var categoryRollup = BuildCategoryRollup(rows);
        var grandTotalMonthlyBudget = new decimal[12];
        var grandTotalMonthlyForecast = new decimal[12];
        var grandTotalMonthlyActual = new decimal[12];
        foreach (var row in rows)
        {
            for (var i = 0; i < 12; i++)
            {
                grandTotalMonthlyBudget[i] += row.Months[i].Budget;
                grandTotalMonthlyForecast[i] += row.Months[i].Forecast;
                grandTotalMonthlyActual[i] += row.Months[i].Result;
            }
        }

        return new StoreBudgetGridDto(
            storeId, fiscalYear, asOf.Year, asOf.Month,
            currentVersion?.Id, currentVersion?.Label,
            rows, categoryRollup,
            grandTotalMonthlyBudget.ToList(), grandTotalMonthlyBudget.Sum(),
            grandTotalMonthlyForecast.ToList(), grandTotalMonthlyForecast.Sum(),
            grandTotalMonthlyActual.ToList(), grandTotalMonthlyActual.Sum(),
            allocatedBudget?.TotalAllocatedBudget);
    }

    private static List<CategoryRollupRowDto> BuildCategoryRollup(List<LineItemGridRowDto> rows)
    {
        var result = new List<CategoryRollupRowDto>();
        foreach (var group in rows.GroupBy(r => r.ExpenseCategory))
        {
            var monthly = new decimal[12];
            foreach (var row in group)
            {
                for (var i = 0; i < 12; i++) monthly[i] += row.Months[i].Budget;
            }

            var q1 = monthly[0] + monthly[1] + monthly[2];
            var q2 = monthly[3] + monthly[4] + monthly[5];
            var q3 = monthly[6] + monthly[7] + monthly[8];
            var q4 = monthly[9] + monthly[10] + monthly[11];

            result.Add(new CategoryRollupRowDto(group.Key, monthly.ToList(), q1, q2, q3, q4, q1 + q2, q3 + q4, q1 + q2 + q3 + q4));
        }
        return result;
    }

    private static bool IsBefore(int year, int month, int otherYear, int otherMonth) =>
        year < otherYear || (year == otherYear && month < otherMonth);

    public async Task<BudgetLineItem> CreateLineItemAsync(int storeId, int fiscalYear, UpsertLineItemRequest request)
    {
        await EnsureWithinAllocatedBudgetAsync(storeId, fiscalYear, excludeLineItemId: null, request.ApprovedAmount);

        var totalCost = Math.Round(request.Units * request.Rate, 2);
        var lineItem = new BudgetLineItem
        {
            StoreId = storeId,
            FiscalYear = fiscalYear,
            GLCode = request.GLCode,
            Description = request.Description,
            CostCenter = request.CostCenter,
            ExpenseCategory = request.ExpenseCategory,
            Units = request.Units,
            Rate = request.Rate,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            NoOfMonths = request.NoOfMonths,
            TotalCost = totalCost,
            ApprovedAmount = request.ApprovedAmount,
            CreatedAt = DateTime.UtcNow
        };
        _db.BudgetLineItems.Add(lineItem);
        await _db.SaveChangesAsync();

        SpreadMonthlyAmounts(lineItem, totalCost);
        await _db.SaveChangesAsync();

        return lineItem;
    }

    public async Task<BudgetLineItem> UpdateLineItemAsync(int lineItemId, UpsertLineItemRequest request)
    {
        var lineItem = await _db.BudgetLineItems.Include(li => li.Months).FirstOrDefaultAsync(li => li.Id == lineItemId)
            ?? throw new ArgumentException($"Line item {lineItemId} not found.");

        await EnsureWithinAllocatedBudgetAsync(lineItem.StoreId, lineItem.FiscalYear, excludeLineItemId: lineItem.Id, request.ApprovedAmount);

        var totalCost = Math.Round(request.Units * request.Rate, 2);

        lineItem.GLCode = request.GLCode;
        lineItem.Description = request.Description;
        lineItem.CostCenter = request.CostCenter;
        lineItem.ExpenseCategory = request.ExpenseCategory;
        lineItem.Units = request.Units;
        lineItem.Rate = request.Rate;
        lineItem.StartDate = request.StartDate;
        lineItem.EndDate = request.EndDate;
        lineItem.NoOfMonths = request.NoOfMonths;
        lineItem.TotalCost = totalCost;
        lineItem.ApprovedAmount = request.ApprovedAmount;

        _db.BudgetLineItemMonths.RemoveRange(lineItem.Months);
        lineItem.Months.Clear();
        await _db.SaveChangesAsync();

        SpreadMonthlyAmounts(lineItem, totalCost);
        await _db.SaveChangesAsync();

        return lineItem;
    }

    // Cross-GL-code check: the sum of every line item's Approved Amount for this
    // store/fiscal year (not any single line's own cap) can't exceed the store's
    // Total Allocated Budget. Skipped entirely if no ceiling has been configured
    // for this store/fiscal year yet.
    private async Task EnsureWithinAllocatedBudgetAsync(int storeId, int fiscalYear, int? excludeLineItemId, decimal newApprovedAmount)
    {
        var allocated = await _db.StoreAllocatedBudgets
            .FirstOrDefaultAsync(a => a.StoreId == storeId && a.FiscalYear == fiscalYear);
        if (allocated == null) return;

        // SQLite's EF Core provider can't translate Sum() over a decimal column
        // server-side, so materialize the (small) list of approved amounts and
        // sum client-side instead.
        var existingAmounts = await _db.BudgetLineItems
            .Where(li => li.StoreId == storeId && li.FiscalYear == fiscalYear && li.Id != (excludeLineItemId ?? -1))
            .Select(li => li.ApprovedAmount)
            .ToListAsync();
        var existingTotal = existingAmounts.Sum();

        var newTotal = existingTotal + newApprovedAmount;
        if (newTotal > allocated.TotalAllocatedBudget)
        {
            throw new InvalidOperationException(
                $"This is more than the allocated budget across lines, not for this particular line: " +
                $"{newTotal.ToString("N0", CultureInfo.InvariantCulture)} AED planned across all GL codes would exceed the store's total allocated budget of " +
                $"{allocated.TotalAllocatedBudget.ToString("N0", CultureInfo.InvariantCulture)} AED for FY{fiscalYear}.");
        }
    }

    // Spreads TotalCost evenly across NoOfMonths consecutive months starting at StartDate,
    // mirroring the client's own Rate x Units -> monthly-spread approach. The last month
    // absorbs any rounding remainder so the months always sum exactly to TotalCost.
    private void SpreadMonthlyAmounts(BudgetLineItem lineItem, decimal totalCost)
    {
        if (lineItem.NoOfMonths <= 0) return;

        var monthlyAmount = Math.Round(totalCost / lineItem.NoOfMonths, 2);
        var remaining = totalCost;
        var current = lineItem.StartDate;

        for (var i = 0; i < lineItem.NoOfMonths; i++)
        {
            var amount = i == lineItem.NoOfMonths - 1 ? remaining : monthlyAmount;
            remaining -= amount;

            _db.BudgetLineItemMonths.Add(new BudgetLineItemMonth
            {
                BudgetLineItemId = lineItem.Id,
                CalendarYear = current.Year,
                CalendarMonth = current.Month,
                Amount = amount
            });

            current = current.AddMonths(1);
        }
    }

    public async Task SaveBudgetCellsAsync(int storeId, int fiscalYear, List<BudgetCellSaveDto> cells, DateOnly asOf)
    {
        if (cells.Count == 0) return;

        foreach (var cell in cells)
        {
            if (IsBefore(cell.CalendarYear, cell.CalendarMonth, asOf.Year, asOf.Month))
            {
                throw new InvalidOperationException($"Cannot edit a closed month ({cell.CalendarYear}-{cell.CalendarMonth:D2}).");
            }
        }

        var lineItemIds = cells.Select(c => c.LineItemId).Distinct().ToList();
        var lineItems = await _db.BudgetLineItems
            .Include(li => li.Months)
            .Where(li => lineItemIds.Contains(li.Id) && li.StoreId == storeId && li.FiscalYear == fiscalYear)
            .ToListAsync();

        var errors = new List<string>();
        foreach (var li in lineItems)
        {
            var monthTotals = li.Months.ToDictionary(m => (m.CalendarYear, m.CalendarMonth), m => m.Amount);
            foreach (var cell in cells.Where(c => c.LineItemId == li.Id))
            {
                monthTotals[(cell.CalendarYear, cell.CalendarMonth)] = cell.Amount;
            }

            var total = monthTotals.Values.Sum();
            if (total > li.ApprovedAmount)
            {
                errors.Add($"{li.GLCode} ({li.Description}): planned total {total:N2} exceeds approved amount {li.ApprovedAmount:N2}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var touchedStoreMonths = new HashSet<(int Year, int Month)>();
        foreach (var cell in cells)
        {
            var li = lineItems.First(l => l.Id == cell.LineItemId);
            var month = li.Months.FirstOrDefault(m => m.CalendarYear == cell.CalendarYear && m.CalendarMonth == cell.CalendarMonth);
            if (month == null)
            {
                _db.BudgetLineItemMonths.Add(new BudgetLineItemMonth
                {
                    BudgetLineItemId = li.Id,
                    CalendarYear = cell.CalendarYear,
                    CalendarMonth = cell.CalendarMonth,
                    Amount = cell.Amount
                });
            }
            else
            {
                month.Amount = cell.Amount;
            }
            touchedStoreMonths.Add((cell.CalendarYear, cell.CalendarMonth));
        }

        await _db.SaveChangesAsync();

        foreach (var (year, month) in touchedStoreMonths)
        {
            var amounts = await _db.BudgetLineItems
                .Where(li => li.StoreId == storeId && li.FiscalYear == fiscalYear)
                .SelectMany(li => li.Months)
                .Where(m => m.CalendarYear == year && m.CalendarMonth == month)
                .Select(m => m.Amount)
                .ToListAsync();
            var sum = amounts.Sum();

            var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.StoreId == storeId && b.Year == year && b.Month == month);
            if (budget == null)
            {
                _db.Budgets.Add(new Budget { StoreId = storeId, Year = year, Month = month, Amount = sum });
            }
            else
            {
                budget.Amount = sum;
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<ForecastRevisionResultDto> ProposeForecastChangesAsync(int storeId, int fiscalYear, List<BudgetCellSaveDto> cells, DateOnly asOf)
    {
        if (cells.Count == 0) return new ForecastRevisionResultDto(new(), new());

        var store = await _db.Stores.FindAsync(storeId)
            ?? throw new ArgumentException($"Store {storeId} not found.");

        foreach (var cell in cells)
        {
            if (IsBefore(cell.CalendarYear, cell.CalendarMonth, asOf.Year, asOf.Month))
            {
                throw new InvalidOperationException($"Cannot revise the forecast for a closed month ({cell.CalendarYear}-{cell.CalendarMonth:D2}).");
            }
        }

        var lineItemIds = cells.Select(c => c.LineItemId).Distinct().ToList();
        var lineItems = await _db.BudgetLineItems
            .Where(li => lineItemIds.Contains(li.Id) && li.StoreId == storeId && li.FiscalYear == fiscalYear)
            .ToListAsync();

        var currentVersion = await _db.ForecastVersions
            .Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.StoreId == storeId && v.FiscalYear == fiscalYear && v.IsCurrent)
            ?? throw new InvalidOperationException("No current forecast version exists for this store/fiscal year. Lock an initial version first.");

        var existingPendingApprovals = await _db.ForecastRevisionApprovals
            .Where(a => a.StoreId == storeId && lineItemIds.Contains(a.BudgetLineItemId) && a.Decision == ApprovalDecision.Pending)
            .ToListAsync();

        var applied = new List<BudgetCellSaveDto>();
        var pendingApprovals = new List<ForecastRevisionApproval>();

        foreach (var li in lineItems)
        {
            var existingLines = currentVersion.Lines
                .Where(l => l.BudgetLineItemId == li.Id)
                .ToDictionary(l => (l.CalendarYear, l.CalendarMonth), l => l.Amount);

            var overlay = new Dictionary<(int, int), decimal>(existingLines);
            var liCells = cells.Where(c => c.LineItemId == li.Id).ToList();
            foreach (var c in liCells) overlay[(c.CalendarYear, c.CalendarMonth)] = c.Amount;

            var newTotal = overlay.Values.Sum();

            if (newTotal <= li.ApprovedAmount)
            {
                foreach (var c in liCells)
                {
                    var line = currentVersion.Lines.FirstOrDefault(l =>
                        l.BudgetLineItemId == li.Id && l.CalendarYear == c.CalendarYear && l.CalendarMonth == c.CalendarMonth);
                    if (line == null)
                    {
                        _db.ForecastVersionLines.Add(new ForecastVersionLine
                        {
                            ForecastVersionId = currentVersion.Id,
                            BudgetLineItemId = li.Id,
                            CalendarYear = c.CalendarYear,
                            CalendarMonth = c.CalendarMonth,
                            Amount = c.Amount
                        });
                    }
                    else
                    {
                        line.Amount = c.Amount;
                    }
                    applied.Add(c);
                }
            }
            else
            {
                // Over the approved cap - route to a 2-level approval (Store Manager, then
                // Regional Manager) instead of applying, mirroring the Expense/Discount pattern.
                foreach (var c in liCells)
                {
                    var previous = existingLines.TryGetValue((c.CalendarYear, c.CalendarMonth), out var prev) ? prev : 0m;

                    // A cell that already has a pending pair sitting on it (from an
                    // earlier proposal not yet decided) gets that pair's amount
                    // updated in place instead of piling up a second, independent
                    // pending pair for the same cell.
                    var existingForCell = existingPendingApprovals
                        .Where(a => a.BudgetLineItemId == li.Id && a.CalendarYear == c.CalendarYear && a.CalendarMonth == c.CalendarMonth)
                        .ToList();
                    if (existingForCell.Count > 0)
                    {
                        foreach (var existing in existingForCell)
                        {
                            existing.ProposedAmount = c.Amount;
                            existing.PreviousAmount = previous;
                        }
                        continue;
                    }

                    pendingApprovals.Add(new ForecastRevisionApproval
                    {
                        StoreId = storeId,
                        BudgetLineItemId = li.Id,
                        CalendarYear = c.CalendarYear,
                        CalendarMonth = c.CalendarMonth,
                        ProposedAmount = c.Amount,
                        PreviousAmount = previous,
                        Level = 1,
                        ApproverRole = "Store Manager",
                        ApproverName = store.StoreManagerName,
                        Decision = ApprovalDecision.Pending,
                        CreatedAt = DateTime.UtcNow
                    });
                    pendingApprovals.Add(new ForecastRevisionApproval
                    {
                        StoreId = storeId,
                        BudgetLineItemId = li.Id,
                        CalendarYear = c.CalendarYear,
                        CalendarMonth = c.CalendarMonth,
                        ProposedAmount = c.Amount,
                        PreviousAmount = previous,
                        Level = 2,
                        ApproverRole = "Regional Manager",
                        ApproverName = store.RegionalManagerName,
                        Decision = ApprovalDecision.Pending,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        if (pendingApprovals.Count > 0)
        {
            _db.ForecastRevisionApprovals.AddRange(pendingApprovals);
        }

        await _db.SaveChangesAsync();

        var pendingDtos = pendingApprovals
            .Where(a => a.Level == 1) // report one card per proposed cell, not one per level
            .Select(a =>
            {
                var li = lineItems.First(l => l.Id == a.BudgetLineItemId);
                return new ForecastRevisionApprovalDto(
                    a.Id, a.StoreId, a.BudgetLineItemId, li.GLCode, li.Description,
                    a.CalendarYear, a.CalendarMonth, a.ProposedAmount, a.PreviousAmount,
                    a.Level, a.ApproverRole, a.ApproverName, a.Decision.ToString(), a.DecidedAt, a.Comments, a.CreatedAt);
            })
            .ToList();

        return new ForecastRevisionResultDto(applied, pendingDtos);
    }

    public async Task<ForecastRevisionApprovalDto> DecideForecastRevisionAsync(int approvalId, bool approve, string? comments)
    {
        var approval = await _db.ForecastRevisionApprovals
            .Include(a => a.BudgetLineItem)
            .FirstOrDefaultAsync(a => a.Id == approvalId)
            ?? throw new ArgumentException($"Forecast revision approval {approvalId} not found.");

        if (approval.Decision != ApprovalDecision.Pending)
        {
            throw new InvalidOperationException("This approval has already been decided.");
        }

        var sibling = await _db.ForecastRevisionApprovals.FirstOrDefaultAsync(a =>
            a.StoreId == approval.StoreId && a.BudgetLineItemId == approval.BudgetLineItemId &&
            a.CalendarYear == approval.CalendarYear && a.CalendarMonth == approval.CalendarMonth &&
            a.ProposedAmount == approval.ProposedAmount && a.Id != approval.Id);

        if (approval.Level == 2 && (sibling == null || sibling.Decision != ApprovalDecision.Approved))
        {
            throw new InvalidOperationException("Level 1 approval must be completed before Level 2 can act.");
        }

        approval.Decision = approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        approval.DecidedAt = DateTime.UtcNow;
        approval.Comments = comments;

        if (!approve && approval.Level == 1 && sibling != null && sibling.Decision == ApprovalDecision.Pending)
        {
            sibling.Decision = ApprovalDecision.Rejected;
            sibling.DecidedAt = DateTime.UtcNow;
            sibling.Comments = "Auto-rejected: Level 1 rejected this forecast revision.";
        }

        var fullyApproved = approve && approval.Level == 2;
        if (fullyApproved)
        {
            var fiscalYear = FiscalCalendarHelper.GetFiscalYear(approval.CalendarYear, approval.CalendarMonth);
            var currentVersion = await _db.ForecastVersions
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.StoreId == approval.StoreId && v.FiscalYear == fiscalYear && v.IsCurrent);

            if (currentVersion != null)
            {
                var line = currentVersion.Lines.FirstOrDefault(l =>
                    l.BudgetLineItemId == approval.BudgetLineItemId && l.CalendarYear == approval.CalendarYear && l.CalendarMonth == approval.CalendarMonth);
                if (line == null)
                {
                    _db.ForecastVersionLines.Add(new ForecastVersionLine
                    {
                        ForecastVersionId = currentVersion.Id,
                        BudgetLineItemId = approval.BudgetLineItemId,
                        CalendarYear = approval.CalendarYear,
                        CalendarMonth = approval.CalendarMonth,
                        Amount = approval.ProposedAmount
                    });
                }
                else
                {
                    line.Amount = approval.ProposedAmount;
                }
            }
        }

        await _db.SaveChangesAsync();

        return new ForecastRevisionApprovalDto(
            approval.Id, approval.StoreId, approval.BudgetLineItemId,
            approval.BudgetLineItem?.GLCode ?? "", approval.BudgetLineItem?.Description ?? "",
            approval.CalendarYear, approval.CalendarMonth, approval.ProposedAmount, approval.PreviousAmount,
            approval.Level, approval.ApproverRole, approval.ApproverName,
            approval.Decision.ToString(), approval.DecidedAt, approval.Comments, approval.CreatedAt);
    }

    public async Task<ForecastVersionDto> LockForecastVersionAsync(int storeId, int fiscalYear, DateOnly asOf, string? label)
    {
        var current = await _db.ForecastVersions
            .Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.StoreId == storeId && v.FiscalYear == fiscalYear && v.IsCurrent)
            ?? throw new InvalidOperationException("No current forecast version exists to lock.");

        current.IsCurrent = false;

        var newVersion = new ForecastVersion
        {
            StoreId = storeId,
            FiscalYear = fiscalYear,
            AsOfCalendarYear = asOf.Year,
            AsOfCalendarMonth = asOf.Month,
            Label = string.IsNullOrWhiteSpace(label) ? $"As of {asOf:MMM-yyyy}" : label,
            CreatedAt = DateTime.UtcNow,
            IsCurrent = true
        };
        _db.ForecastVersions.Add(newVersion);
        await _db.SaveChangesAsync();

        foreach (var line in current.Lines)
        {
            _db.ForecastVersionLines.Add(new ForecastVersionLine
            {
                ForecastVersionId = newVersion.Id,
                BudgetLineItemId = line.BudgetLineItemId,
                CalendarYear = line.CalendarYear,
                CalendarMonth = line.CalendarMonth,
                Amount = line.Amount
            });
        }
        await _db.SaveChangesAsync();

        return new ForecastVersionDto(newVersion.Id, newVersion.Label, newVersion.AsOfCalendarYear, newVersion.AsOfCalendarMonth, newVersion.CreatedAt, true);
    }

    public async Task<List<ForecastVersionDto>> GetForecastVersionsAsync(int storeId, int fiscalYear)
    {
        var versions = await _db.ForecastVersions
            .Where(v => v.StoreId == storeId && v.FiscalYear == fiscalYear)
            .OrderByDescending(v => v.AsOfCalendarYear)
            .ThenByDescending(v => v.AsOfCalendarMonth)
            .ToListAsync();

        return versions.Select(v => new ForecastVersionDto(v.Id, v.Label, v.AsOfCalendarYear, v.AsOfCalendarMonth, v.CreatedAt, v.IsCurrent)).ToList();
    }
}
