using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Dtos;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;

    public AuthController(AppDbContext db, JwtTokenService jwtTokenService)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequest request)
    {
        var user = await _db.Users
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user == null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid username or password." });
        }

        var token = _jwtTokenService.GenerateToken(user);
        var userDto = new AuthUserDto(user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.StoreId, user.Store?.Name);
        return Ok(new LoginResponseDto(token, userDto));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.Include(u => u.Store).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound();

        return Ok(new AuthUserDto(user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.StoreId, user.Store?.Name));
    }
}
