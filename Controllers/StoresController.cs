using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Models;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StoresController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;

    public StoresController(AppDbContext db, CurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<StoreDto>>> GetStores()
    {
        var query = _db.Stores.AsQueryable();

        // Store Managers only ever see the one store they're assigned to.
        if (!_currentUser.IsRegionalManager)
        {
            query = query.Where(s => s.Id == _currentUser.StoreId);
        }

        var stores = await query
            .Select(s => new StoreDto(s.Id, s.Name, s.City, s.RegionName, s.StoreManagerName, s.RegionalManagerName))
            .ToListAsync();
        return Ok(stores);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<ApplianceCategoryDto>>> GetCategories()
    {
        var categories = await _db.ApplianceCategories
            .Select(c => new ApplianceCategoryDto(c.Id, c.Name))
            .ToListAsync();
        return Ok(categories);
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPost]
    public async Task<ActionResult<StoreDto>> CreateStore([FromBody] CreateStoreRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Username == request.ManagerUsername))
        {
            return BadRequest(new { error = $"Username '{request.ManagerUsername}' is already taken." });
        }

        var store = new Store
        {
            Name = request.Name,
            City = request.City,
            RegionName = request.RegionName,
            StoreManagerName = request.StoreManagerName,
            RegionalManagerName = request.RegionalManagerName
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        _db.Users.Add(new User
        {
            Username = request.ManagerUsername,
            PasswordHash = PasswordHasher.Hash(request.ManagerPassword),
            DisplayName = request.StoreManagerName,
            Role = UserRole.StoreManager,
            StoreId = store.Id
        });
        await _db.SaveChangesAsync();

        return Ok(new StoreDto(store.Id, store.Name, store.City, store.RegionName, store.StoreManagerName, store.RegionalManagerName));
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpPut("{id}")]
    public async Task<ActionResult<StoreDto>> UpdateStore(int id, [FromBody] UpdateStoreRequest request)
    {
        var store = await _db.Stores.FindAsync(id);
        if (store == null) return NotFound(new { error = $"Store {id} not found." });

        store.Name = request.Name;
        store.City = request.City;
        store.RegionName = request.RegionName;
        store.StoreManagerName = request.StoreManagerName;
        store.RegionalManagerName = request.RegionalManagerName;
        await _db.SaveChangesAsync();

        return Ok(new StoreDto(store.Id, store.Name, store.City, store.RegionName, store.StoreManagerName, store.RegionalManagerName));
    }

    [Authorize(Roles = "RegionalManager")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteStore(int id)
    {
        var store = await _db.Stores.FindAsync(id);
        if (store == null) return NotFound(new { error = $"Store {id} not found." });

        _db.Stores.Remove(store);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
