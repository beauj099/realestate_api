namespace RealEstateApi.Application.DTOs;

public record UpsertValuationRequest(
    decimal? OwnersNetPrice,
    decimal? AgentValuation,
    decimal? CommissionPercent
);

public record ValuationDto(
    int Id,
    decimal? OwnersNetPrice,
    decimal? AgentValuation,
    decimal? CommissionPercent
);

public record UpsertRunningCostsRequest(
    decimal? MonthlyLevy,
    decimal? MonthlyRates,
    decimal? Electricity,
    decimal? Water,
    decimal? MunicipalAccount = null
);

public record RunningCostsDto(
    int Id,
    int ListingId,
    decimal? MonthlyLevy,
    decimal? MonthlyRates,
    decimal? Electricity,
    decimal? Water,
    decimal? MunicipalAccount = null
);
