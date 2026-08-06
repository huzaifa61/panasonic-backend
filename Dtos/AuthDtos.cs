namespace PanasonicPrototype.Api.Dtos;

public record LoginRequest(string Username, string Password);

public record AuthUserDto(int UserId, string Username, string DisplayName, string Role, int? StoreId, string? StoreName);

public record LoginResponseDto(string Token, AuthUserDto User);
