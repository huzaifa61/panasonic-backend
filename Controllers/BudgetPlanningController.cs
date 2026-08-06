using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/budget-planning")]
public class BudgetPlanningController : ControllerBase
{
    private readonly BudgetPlanningService _service;
    private readonly CurrentUserContext _currentUser;

    public BudgetPlanningController(BudgetPlanningService service, CurrentUserContext currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    // Read-only - available to Store Managers for their own store too (e.g. the
    // Dashboard's FY rollup panel), unlike every mutating action below which stays
    // Regional-Manager-only.
    [HttpGet("{storeId}/grid")]
    public async Task<ActionResult<StoreBudgetGridDto>> GetGrid(int storeId, [FromQuery] int fiscalYear, [FromQuery] DateOnly? asOf = null)
    {
        try
        {
            _currentUser.EnsureStoreAccess(storeId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }

        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await _service.GetGridAsync(storeId, fiscalYear, effectiveAsOf));
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPost("{storeId}/line-items")]
    public async Task<IActionResult> CreateLineItem(int storeId, [FromQuery] int fiscalYear, [FromBody] UpsertLineItemRequest request)
    {
        try
        {
            var lineItem = await _service.CreateLineItemAsync(storeId, fiscalYear, request);
            return Ok(new { lineItem.Id, lineItem.GLCode, lineItem.Description, lineItem.TotalCost, lineItem.ApprovedAmount });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return BadRequest(new { error = $"A line item with GL Code '{request.GLCode}' already exists for this store/fiscal year." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPut("{storeId}/line-items/{lineItemId}")]
    public async Task<IActionResult> UpdateLineItem(int storeId, int lineItemId, [FromBody] UpsertLineItemRequest request)
    {
        try
        {
            var lineItem = await _service.UpdateLineItemAsync(lineItemId, request);
            return Ok(new { lineItem.Id, lineItem.GLCode, lineItem.Description, lineItem.TotalCost, lineItem.ApprovedAmount });
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

    [Authorize(Roles = "RegionalManager")]
    [HttpPut("{storeId}/grid")]
    public async Task<IActionResult> SaveBudgetGrid(int storeId, [FromQuery] int fiscalYear, [FromQuery] DateOnly? asOf, [FromBody] SaveBudgetGridRequest request)
    {
        try
        {
            var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            await _service.SaveBudgetCellsAsync(storeId, fiscalYear, request.Cells, effectiveAsOf);
            return Ok(await _service.GetGridAsync(storeId, fiscalYear, effectiveAsOf));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPost("{storeId}/forecast-revisions")]
    public async Task<ActionResult<ForecastRevisionResultDto>> ProposeForecastChanges(
        int storeId, [FromQuery] int fiscalYear, [FromQuery] DateOnly? asOf, [FromBody] ProposeForecastRequest request)
    {
        try
        {
            var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var result = await _service.ProposeForecastChangesAsync(storeId, fiscalYear, request.Cells, effectiveAsOf);
            return Ok(result);
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

    // Note: forecast revision decisions are made through ApprovalsController.Decide
    // (which enforces the same Level 1 Store Manager / Level 2 Regional Manager
    // gating as Expense/Discount approvals), not here.

    [Authorize(Roles = "RegionalManager")]
    [HttpPost("{storeId}/forecast-versions/lock")]
    public async Task<ActionResult<ForecastVersionDto>> LockForecastVersion(
        int storeId, [FromQuery] int fiscalYear, [FromQuery] DateOnly? asOf, [FromBody] LockForecastRequest request)
    {
        try
        {
            var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return Ok(await _service.LockForecastVersionAsync(storeId, fiscalYear, effectiveAsOf, request.Label));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{storeId}/forecast-versions")]
    public async Task<ActionResult<List<ForecastVersionDto>>> GetForecastVersions(int storeId, [FromQuery] int fiscalYear)
    {
        try
        {
            _currentUser.EnsureStoreAccess(storeId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }

        return Ok(await _service.GetForecastVersionsAsync(storeId, fiscalYear));
    }
}
