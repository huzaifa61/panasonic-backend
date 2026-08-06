using System.Security.Claims;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

public class CurrentUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal Principal => _accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context available.");

    public int UserId => int.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public UserRole Role => Enum.Parse<UserRole>(Principal.FindFirstValue(ClaimTypes.Role)!);

    public int? StoreId
    {
        get
        {
            var value = Principal.FindFirstValue("storeId");
            return value == null ? null : int.Parse(value);
        }
    }

    public bool IsRegionalManager => Role == UserRole.RegionalManager;

    // Throws if a Store Manager tries to act on a store other than their own.
    // Regional Managers pass through unchecked (access to all stores).
    public void EnsureStoreAccess(int storeId)
    {
        if (Role == UserRole.StoreManager && StoreId != storeId)
        {
            throw new UnauthorizedAccessException("You do not have access to this store.");
        }
    }
}
