using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[Authorize(Roles = "RegionalManager")]
[ApiController]
[Route("api/global-planning")]
public class GlobalPlanningController : ControllerBase
{
    private readonly GlobalPlanningService _service;

    public GlobalPlanningController(GlobalPlanningService service)
    {
        _service = service;
    }

    [HttpGet("grid")]
    public async Task<ActionResult<List<GlobalPlanningStoreDto>>> GetGrid(
        [FromQuery] int fiscalYear, [FromQuery] int? storeId, [FromQuery] DateOnly? asOf = null)
    {
        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await _service.GetGridAsync(fiscalYear, effectiveAsOf, storeId));
    }

    [HttpPut("cells")]
    public async Task<IActionResult> SaveCells([FromQuery] DateOnly? asOf, [FromBody] SaveGlobalPlanningRequest request)
    {
        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            await _service.SaveCellsAsync(request.Cells, effectiveAsOf);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
