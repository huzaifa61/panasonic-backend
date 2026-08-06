namespace PanasonicPrototype.Api.Dtos;

public record StoreDto(int Id, string Name, string City, string RegionName, string StoreManagerName, string RegionalManagerName);

public record ApplianceCategoryDto(int Id, string Name);
