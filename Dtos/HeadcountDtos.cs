namespace PanasonicPrototype.Api.Dtos;

public record HeadcountDto(
    int StoreId,
    string StoreName,
    int Year,
    int Month,
    int CurrentHeadcount,
    int ForecastedHeadcountNeed,
    int Gap);
