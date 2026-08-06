using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HeadcountController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;

    public HeadcountController(AppDbContext db, CurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<HeadcountDto>>> GetHeadcount([FromQuery] int year, [FromQuery] int month)
    {
        var query = _db.Headcounts.Include(h => h.Store).Where(h => h.Year == year && h.Month == month);

        if (!_currentUser.IsRegionalManager)
        {
            query = query.Where(h => h.StoreId == _currentUser.StoreId);
        }

        var rows = await query.ToListAsync();

        return Ok(rows.Select(h => new HeadcountDto(
            h.StoreId,
            h.Store?.Name ?? "",
            h.Year,
            h.Month,
            h.CurrentHeadcount,
            h.ForecastedHeadcountNeed,
            h.ForecastedHeadcountNeed - h.CurrentHeadcount)).ToList());
    }
}
