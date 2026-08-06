using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("approval-threshold")]
    public async Task<ActionResult<ApprovalSettingsDto>> GetApprovalThreshold()
    {
        var settings = await _db.ApprovalSettings.FirstOrDefaultAsync();
        return Ok(new ApprovalSettingsDto(settings?.ExpenseApprovalThreshold ?? 0m));
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPut("approval-threshold")]
    public async Task<ActionResult<ApprovalSettingsDto>> UpdateApprovalThreshold([FromBody] UpdateApprovalSettingsRequest request)
    {
        var settings = await _db.ApprovalSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new ApprovalSettings { ExpenseApprovalThreshold = request.ExpenseApprovalThreshold };
            _db.ApprovalSettings.Add(settings);
        }
        else
        {
            settings.ExpenseApprovalThreshold = request.ExpenseApprovalThreshold;
        }

        await _db.SaveChangesAsync();
        return Ok(new ApprovalSettingsDto(settings.ExpenseApprovalThreshold));
    }
}
