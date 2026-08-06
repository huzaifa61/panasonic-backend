namespace PanasonicPrototype.Api.Models;

public enum UserRole
{
    StoreManager,
    RegionalManager
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    // Set for StoreManager (the one store they manage); null for RegionalManager (access to all stores).
    public int? StoreId { get; set; }
    public Store? Store { get; set; }
}
