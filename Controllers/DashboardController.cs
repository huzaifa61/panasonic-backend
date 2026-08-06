using Microsoft.AspNetCore.Mvc;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;
    private readonly CurrentUserContext _currentUser;

    public DashboardController(DashboardService dashboardService, CurrentUserContext currentUser)
    {
        _dashboardService = dashboardService;
        _currentUser = currentUser;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<OverviewDto>> GetOverview([FromQuery] int year, [FromQuery] int month, [FromQuery] DateOnly? asOf = null)
    {
        // Store Managers get an "overview" scoped to just their own store.
        var storeIdFilter = _currentUser.IsRegionalManager ? (int?)null : _currentUser.StoreId;
        return Ok(await _dashboardService.GetOverviewAsync(year, month, asOf, storeIdFilter));
    }

    [HttpGet("store/{storeId}")]
    public async Task<ActionResult<StoreDashboardDto>> GetStoreDashboard(int storeId, [FromQuery] int year, [FromQuery] int month, [FromQuery] DateOnly? asOf = null)
    {
        try
        {
            _currentUser.EnsureStoreAccess(storeId);
            return Ok(await _dashboardService.GetStoreDashboardAsync(storeId, year, month, asOf));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpGet("trend")]
    public async Task<ActionResult<List<MonthlyTrendDto>>> GetTrend(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int? storeId, [FromQuery] int monthsBack = 4)
    {
        var effectiveStoreId = _currentUser.IsRegionalManager ? storeId : _currentUser.StoreId;
        try
        {
            return Ok(await _dashboardService.GetTrendAsync(effectiveStoreId, year, month, monthsBack));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("category-breakdown")]
    public async Task<ActionResult<List<CategoryBreakdownDto>>> GetCategoryBreakdown(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int? storeId)
    {
        var effectiveStoreId = _currentUser.IsRegionalManager ? storeId : _currentUser.StoreId;
        return Ok(await _dashboardService.GetCategoryBreakdownAsync(effectiveStoreId, year, month));
    }

    [HttpGet("category-margin")]
    public async Task<ActionResult<List<CategoryMarginDto>>> GetCategoryMargin(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int? storeId)
    {
        var effectiveStoreId = _currentUser.IsRegionalManager ? storeId : _currentUser.StoreId;
        return Ok(await _dashboardService.GetCategoryMarginAsync(effectiveStoreId, year, month));
    }
}
