using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SalesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DiscountApprovalService _discountApprovalService;
    private readonly CurrentUserContext _currentUser;

    public SalesController(AppDbContext db, DiscountApprovalService discountApprovalService, CurrentUserContext currentUser)
    {
        _db = db;
        _discountApprovalService = discountApprovalService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<SaleDto>>> GetSales([FromQuery] int? storeId, [FromQuery] int? year, [FromQuery] int? month)
    {
        var query = _db.Sales.Include(s => s.Store).AsQueryable();

        if (!_currentUser.IsRegionalManager)
        {
            query = query.Where(s => s.StoreId == _currentUser.StoreId);
        }
        else if (storeId.HasValue)
        {
            query = query.Where(s => s.StoreId == storeId.Value);
        }

        if (year.HasValue) query = query.Where(s => s.Date.Year == year.Value);
        if (month.HasValue) query = query.Where(s => s.Date.Month == month.Value);

        var sales = await query.OrderByDescending(s => s.Date).ToListAsync();
        return Ok(sales.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SaleDto>> CreateSale([FromBody] CreateSaleRequest request)
    {
        try
        {
            _currentUser.EnsureStoreAccess(request.StoreId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }

        var sale = await _discountApprovalService.CreateSaleAsync(
            request.StoreId, request.CustomerName, request.SaleAmount, request.RequestedDiscountPercent, request.Date);

        var loaded = await _db.Sales.Include(s => s.Store).FirstAsync(s => s.Id == sale.Id);
        return Ok(ToDto(loaded));
    }

    private static SaleDto ToDto(Models.Sale s) => new(
        s.Id, s.StoreId, s.Store?.Name ?? "", s.CustomerName, s.SaleAmount, s.RequestedDiscountPercent, s.Date, s.Status.ToString());
}
