using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApprovalsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BudgetApprovalService _approvalService;
    private readonly DiscountApprovalService _discountApprovalService;
    private readonly BudgetPlanningService _budgetPlanningService;
    private readonly CurrentUserContext _currentUser;

    public ApprovalsController(
        AppDbContext db, BudgetApprovalService approvalService, DiscountApprovalService discountApprovalService,
        BudgetPlanningService budgetPlanningService, CurrentUserContext currentUser)
    {
        _db = db;
        _approvalService = approvalService;
        _discountApprovalService = discountApprovalService;
        _budgetPlanningService = budgetPlanningService;
        _currentUser = currentUser;
    }

    [HttpGet("pending")]
    public async Task<ActionResult<List<PendingApprovalDto>>> GetPending([FromQuery] int? storeId)
    {
        // Store Managers only ever see their own store's Level 1 queue - Level 2
        // (Regional Manager) approvals are never actionable by them, so never shown.
        var isStoreManager = !_currentUser.IsRegionalManager;
        var effectiveStoreId = isStoreManager ? _currentUser.StoreId : storeId;

        var expenseQuery = _db.Approvals
            .Include(a => a.Expense)
            .ThenInclude(e => e!.Store)
            .Include(a => a.Expense)
            .ThenInclude(e => e!.Category)
            .Where(a => a.Decision == Models.ApprovalDecision.Pending);

        if (effectiveStoreId.HasValue)
        {
            expenseQuery = expenseQuery.Where(a => a.Expense!.StoreId == effectiveStoreId.Value);
        }
        if (isStoreManager)
        {
            expenseQuery = expenseQuery.Where(a => a.Level == 1);
        }

        var expenseApprovals = await expenseQuery.ToListAsync();

        // Only surface the approval that's actually actionable right now:
        // Level 2 stays hidden until Level 1 has been approved for that expense.
        var actionableExpenseApprovals = expenseApprovals.Where(a =>
            (a.Level == 1 && a.Expense!.Status == Models.ExpenseStatus.PendingLevel1Approval) ||
            (a.Level == 2 && a.Expense!.Status == Models.ExpenseStatus.PendingLevel2Approval));

        var expensePending = actionableExpenseApprovals.Select(a => new PendingApprovalDto(
            a.Id,
            "Expense",
            a.Level,
            a.ApproverRole,
            a.ApproverName,
            a.Expense!.Id,
            null,
            a.Expense.StoreId,
            a.Expense.Store?.Name ?? "",
            a.Expense.Category?.Name ?? "",
            a.Expense.Description,
            a.Expense.Amount,
            a.Expense.Date,
            null,
            null));

        var discountQuery = _db.DiscountApprovals
            .Include(a => a.Sale)
            .ThenInclude(s => s!.Store)
            .Where(a => a.Decision == Models.ApprovalDecision.Pending);

        if (effectiveStoreId.HasValue)
        {
            discountQuery = discountQuery.Where(a => a.Sale!.StoreId == effectiveStoreId.Value);
        }
        if (isStoreManager)
        {
            discountQuery = discountQuery.Where(a => a.Level == 1);
        }

        var discountApprovals = await discountQuery.ToListAsync();

        var actionableDiscountApprovals = discountApprovals.Where(a =>
            (a.Level == 1 && a.Sale!.Status == Models.SaleStatus.PendingLevel1Approval) ||
            (a.Level == 2 && a.Sale!.Status == Models.SaleStatus.PendingLevel2Approval));

        var discountPending = actionableDiscountApprovals.Select(a => new PendingApprovalDto(
            a.Id,
            "Discount",
            a.Level,
            a.ApproverRole,
            a.ApproverName,
            null,
            a.Sale!.Id,
            a.Sale.StoreId,
            a.Sale.Store?.Name ?? "",
            null,
            null,
            a.Sale.SaleAmount,
            a.Sale.Date,
            a.Sale.CustomerName,
            a.Sale.RequestedDiscountPercent));

        var forecastQuery = _db.ForecastRevisionApprovals
            .Include(a => a.Store)
            .Include(a => a.BudgetLineItem)
            .Where(a => a.Decision == Models.ApprovalDecision.Pending);

        if (effectiveStoreId.HasValue)
        {
            forecastQuery = forecastQuery.Where(a => a.StoreId == effectiveStoreId.Value);
        }
        if (isStoreManager)
        {
            forecastQuery = forecastQuery.Where(a => a.Level == 1);
        }

        var forecastApprovals = await forecastQuery.ToListAsync();

        // Level 1 is always actionable while pending; Level 2 only once its Level 1
        // sibling (same store/line item/month/proposed amount) has been approved.
        var actionableForecastApprovals = new List<Models.ForecastRevisionApproval>();
        foreach (var a in forecastApprovals)
        {
            if (a.Level == 1)
            {
                actionableForecastApprovals.Add(a);
                continue;
            }

            var siblingApproved = await _db.ForecastRevisionApprovals.AnyAsync(s =>
                s.StoreId == a.StoreId && s.BudgetLineItemId == a.BudgetLineItemId &&
                s.CalendarYear == a.CalendarYear && s.CalendarMonth == a.CalendarMonth &&
                s.ProposedAmount == a.ProposedAmount && s.Level == 1 && s.Decision == Models.ApprovalDecision.Approved);

            if (siblingApproved)
            {
                actionableForecastApprovals.Add(a);
            }
        }

        var forecastPending = actionableForecastApprovals.Select(a => new PendingApprovalDto(
            a.Id,
            "ForecastRevision",
            a.Level,
            a.ApproverRole,
            a.ApproverName,
            null,
            null,
            a.StoreId,
            a.Store?.Name ?? "",
            a.BudgetLineItem?.ExpenseCategory,
            a.BudgetLineItem?.Description,
            a.ProposedAmount,
            new DateOnly(a.CalendarYear, a.CalendarMonth, 1),
            null,
            null,
            a.BudgetLineItemId,
            a.BudgetLineItem?.GLCode,
            a.PreviousAmount));

        var merged = expensePending.Concat(discountPending).Concat(forecastPending).OrderBy(a => a.Date).ToList();
        return Ok(merged);
    }

    [HttpPost("{id}/decide")]
    public async Task<IActionResult> Decide(int id, [FromQuery] string type, [FromBody] DecideApprovalRequest request)
    {
        var isDiscount = string.Equals(type, "discount", StringComparison.OrdinalIgnoreCase);
        var isForecastRevision = string.Equals(type, "forecastrevision", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isForecastRevision)
            {
                var forecastApproval = await _db.ForecastRevisionApprovals.FirstOrDefaultAsync(a => a.Id == id);
                if (forecastApproval == null) return NotFound(new { error = $"Forecast revision approval {id} not found." });

                if (!AllowedToDecide(forecastApproval.Level, forecastApproval.StoreId))
                {
                    return StatusCode(403, new { error = "You are not authorized to act on this approval." });
                }

                var decidedForecast = await _budgetPlanningService.DecideForecastRevisionAsync(id, request.Approve, request.Comments);
                return Ok(decidedForecast);
            }

            if (isDiscount)
            {
                var discountApproval = await _db.DiscountApprovals.Include(a => a.Sale)
                    .FirstOrDefaultAsync(a => a.Id == id);
                if (discountApproval == null) return NotFound(new { error = $"Discount approval {id} not found." });

                if (!AllowedToDecide(discountApproval.Level, discountApproval.Sale!.StoreId))
                {
                    return StatusCode(403, new { error = "You are not authorized to act on this approval." });
                }

                var decided = await _discountApprovalService.DecideDiscountApprovalAsync(id, request.Approve, request.Comments);
                return Ok(new ApprovalDto(
                    decided.Id, decided.Level, decided.ApproverRole, decided.ApproverName,
                    decided.Decision.ToString(), decided.DecidedAt, decided.Comments));
            }

            var approval = await _db.Approvals.Include(a => a.Expense).FirstOrDefaultAsync(a => a.Id == id);
            if (approval == null) return NotFound(new { error = $"Approval {id} not found." });

            if (!AllowedToDecide(approval.Level, approval.Expense!.StoreId))
            {
                return StatusCode(403, new { error = "You are not authorized to act on this approval." });
            }

            var decidedApproval = await _approvalService.DecideApprovalAsync(id, request.Approve, request.Comments);
            return Ok(new ApprovalDto(
                decidedApproval.Id, decidedApproval.Level, decidedApproval.ApproverRole, decidedApproval.ApproverName,
                decidedApproval.Decision.ToString(), decidedApproval.DecidedAt, decidedApproval.Comments));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Store Managers may only decide Level 1 approvals for their own store;
    // Regional Managers may decide any level for any store.
    private bool AllowedToDecide(int level, int storeId)
    {
        if (_currentUser.IsRegionalManager) return true;
        return level == 1 && _currentUser.StoreId == storeId;
    }
}
