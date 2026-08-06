using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RevenuesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;

    public RevenuesController(AppDbContext db, CurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<RevenueDto>>> GetRevenues([FromQuery] int? storeId, [FromQuery] int? year, [FromQuery] int? month)
    {
        var query = _db.Revenues.Include(r => r.Store).Include(r => r.Category).AsQueryable();

        if (!_currentUser.IsRegionalManager)
        {
            query = query.Where(r => r.StoreId == _currentUser.StoreId);
        }
        else if (storeId.HasValue)
        {
            query = query.Where(r => r.StoreId == storeId.Value);
        }

        if (year.HasValue) query = query.Where(r => r.Date.Year == year.Value);
        if (month.HasValue) query = query.Where(r => r.Date.Month == month.Value);

        var revenues = await query.OrderByDescending(r => r.Date).ToListAsync();
        return Ok(revenues.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<RevenueDto>> CreateRevenue([FromBody] CreateRevenueRequest request)
    {
        try
        {
            _currentUser.EnsureStoreAccess(request.StoreId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }

        if (!await _db.Stores.AnyAsync(s => s.Id == request.StoreId))
        {
            return NotFound(new { error = $"Store {request.StoreId} not found." });
        }

        var revenue = new Revenue
        {
            StoreId = request.StoreId,
            CategoryId = request.CategoryId,
            Amount = request.Amount,
            Date = request.Date,
            Description = request.Description
        };
        _db.Revenues.Add(revenue);
        await _db.SaveChangesAsync();

        var loaded = await _db.Revenues.Include(r => r.Store).Include(r => r.Category).FirstAsync(r => r.Id == revenue.Id);
        return Ok(ToDto(loaded));
    }

    // Bulk CSV upload: each row is resolved (Store name, Category name) and created
    // the same way as a single revenue entry - a row that fails validation just gets
    // its own error, the rest of the batch still goes through.
    [HttpPost("bulk")]
    public async Task<ActionResult<List<BulkRevenueRowResult>>> BulkCreateRevenues([FromBody] List<BulkRevenueRowRequest> rows)
    {
        var results = new List<BulkRevenueRowResult>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                var store = await _db.Stores.FirstOrDefaultAsync(s => s.Name.ToLower() == row.StoreName.Trim().ToLower());
                if (store == null)
                {
                    results.Add(new BulkRevenueRowResult(i, false, $"Store '{row.StoreName}' not found.", null));
                    continue;
                }

                try
                {
                    _currentUser.EnsureStoreAccess(store.Id);
                }
                catch (UnauthorizedAccessException)
                {
                    results.Add(new BulkRevenueRowResult(i, false, $"You do not have access to store '{row.StoreName}'.", null));
                    continue;
                }

                var category = await _db.ApplianceCategories.FirstOrDefaultAsync(c => c.Name.ToLower() == row.CategoryName.Trim().ToLower());
                if (category == null)
                {
                    results.Add(new BulkRevenueRowResult(i, false, $"Category '{row.CategoryName}' not found.", null));
                    continue;
                }

                var revenue = new Revenue
                {
                    StoreId = store.Id,
                    CategoryId = category.Id,
                    Amount = row.Amount,
                    Date = row.Date,
                    Description = row.Description
                };
                _db.Revenues.Add(revenue);
                await _db.SaveChangesAsync();

                var loaded = await _db.Revenues.Include(r => r.Store).Include(r => r.Category).FirstAsync(r => r.Id == revenue.Id);
                results.Add(new BulkRevenueRowResult(i, true, null, ToDto(loaded)));
            }
            catch (Exception ex)
            {
                results.Add(new BulkRevenueRowResult(i, false, ex.Message, null));
            }
        }

        return Ok(results);
    }

    private static RevenueDto ToDto(Revenue r) =>
        new(r.Id, r.StoreId, r.Store?.Name ?? "", r.CategoryId, r.Category?.Name ?? "", r.Amount, r.Date, r.Description);
}
