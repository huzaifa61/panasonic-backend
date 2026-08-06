using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExpensesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BudgetApprovalService _approvalService;
    private readonly CurrentUserContext _currentUser;

    public ExpensesController(AppDbContext db, BudgetApprovalService approvalService, CurrentUserContext currentUser)
    {
        _db = db;
        _approvalService = approvalService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<ExpenseDto>>> GetExpenses([FromQuery] int? storeId, [FromQuery] int? year, [FromQuery] int? month)
    {
        var query = _db.Expenses
            .Include(e => e.Store)
            .Include(e => e.Category)
            .Include(e => e.Approvals)
            .Include(e => e.BudgetLineItem)
            .AsQueryable();

        if (!_currentUser.IsRegionalManager)
        {
            // Store Managers can only ever see their own store's expenses, regardless
            // of what storeId they pass (or omit).
            query = query.Where(e => e.StoreId == _currentUser.StoreId);
        }
        else if (storeId.HasValue)
        {
            query = query.Where(e => e.StoreId == storeId.Value);
        }

        if (year.HasValue) query = query.Where(e => e.Date.Year == year.Value);
        if (month.HasValue) query = query.Where(e => e.Date.Month == month.Value);

        var expenses = await query.OrderByDescending(e => e.Date).ToListAsync();
        return Ok(expenses.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> CreateExpense([FromBody] CreateExpenseRequest request)
    {
        try
        {
            _currentUser.EnsureStoreAccess(request.StoreId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }

        var (expense, approvalReason) = await _approvalService.CreateExpenseAsync(
            request.StoreId, request.CategoryId, request.Description, request.Amount, request.Date, request.LineItemId);

        var loaded = await _db.Expenses
            .Include(e => e.Store)
            .Include(e => e.Category)
            .Include(e => e.Approvals)
            .Include(e => e.BudgetLineItem)
            .FirstAsync(e => e.Id == expense.Id);

        return Ok(ToDto(loaded) with { ApprovalReason = approvalReason });
    }

    // Bulk CSV upload: each row is resolved (Store name, Category name, optional Line
    // Item GL Code) and run through the exact same CreateExpenseAsync validation/approval
    // logic as a single expense - a row that would need approval still gets created,
    // just already flagged pending, same as if it had been submitted one at a time.
    [HttpPost("bulk")]
    public async Task<ActionResult<List<BulkExpenseRowResult>>> BulkCreateExpenses([FromBody] List<BulkExpenseRowRequest> rows)
    {
        var results = new List<BulkExpenseRowResult>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                var store = await _db.Stores.FirstOrDefaultAsync(s => s.Name.ToLower() == row.StoreName.Trim().ToLower());
                if (store == null)
                {
                    results.Add(new BulkExpenseRowResult(i, false, $"Store '{row.StoreName}' not found.", null));
                    continue;
                }

                try
                {
                    _currentUser.EnsureStoreAccess(store.Id);
                }
                catch (UnauthorizedAccessException)
                {
                    results.Add(new BulkExpenseRowResult(i, false, $"You do not have access to store '{row.StoreName}'.", null));
                    continue;
                }

                var category = await _db.ApplianceCategories.FirstOrDefaultAsync(c => c.Name.ToLower() == row.CategoryName.Trim().ToLower());
                if (category == null)
                {
                    results.Add(new BulkExpenseRowResult(i, false, $"Category '{row.CategoryName}' not found.", null));
                    continue;
                }

                int? lineItemId = null;
                if (!string.IsNullOrWhiteSpace(row.LineItemGLCode))
                {
                    var lineItem = await _db.BudgetLineItems.FirstOrDefaultAsync(li =>
                        li.StoreId == store.Id && li.GLCode.ToLower() == row.LineItemGLCode.Trim().ToLower());
                    if (lineItem == null)
                    {
                        results.Add(new BulkExpenseRowResult(i, false, $"Line item '{row.LineItemGLCode}' not found for store '{row.StoreName}'.", null));
                        continue;
                    }
                    lineItemId = lineItem.Id;
                }

                var (expense, approvalReason) = await _approvalService.CreateExpenseAsync(
                    store.Id, category.Id, row.Description, row.Amount, row.Date, lineItemId);

                var loaded = await _db.Expenses
                    .Include(e => e.Store)
                    .Include(e => e.Category)
                    .Include(e => e.Approvals)
                    .Include(e => e.BudgetLineItem)
                    .FirstAsync(e => e.Id == expense.Id);

                results.Add(new BulkExpenseRowResult(i, true, null, ToDto(loaded) with { ApprovalReason = approvalReason }));
            }
            catch (Exception ex)
            {
                results.Add(new BulkExpenseRowResult(i, false, ex.Message, null));
            }
        }

        return Ok(results);
    }

    private static ExpenseDto ToDto(Models.Expense e) => new(
        e.Id,
        e.StoreId,
        e.Store?.Name ?? "",
        e.Category?.Name ?? "",
        e.Description,
        e.Amount,
        e.Date,
        e.Status.ToString(),
        e.Approvals
            .OrderBy(a => a.Level)
            .Select(a => new ApprovalDto(a.Id, a.Level, a.ApproverRole, a.ApproverName, a.Decision.ToString(), a.DecidedAt, a.Comments))
            .ToList(),
        e.BudgetLineItemId,
        e.BudgetLineItem?.GLCode);
}
