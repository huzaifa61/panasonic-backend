using System.Text;
using Microsoft.AspNetCore.Mvc;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ReportExportService _reportExportService;
    private readonly CurrentUserContext _currentUser;

    public ReportsController(ReportExportService reportExportService, CurrentUserContext currentUser)
    {
        _reportExportService = reportExportService;
        _currentUser = currentUser;
    }

    [HttpGet("store/{storeId}/export")]
    public async Task<IActionResult> ExportStoreReport(int storeId, [FromQuery] int year, [FromQuery] int month)
    {
        try
        {
            _currentUser.EnsureStoreAccess(storeId);
            var csv = await _reportExportService.BuildStoreReportCsvAsync(storeId, year, month);
            var bytes = Encoding.UTF8.GetBytes(csv);
            var fileName = $"store-{storeId}-report-{year}-{month:D2}.csv";
            return File(bytes, "text/csv", fileName);
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
}
