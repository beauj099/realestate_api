namespace RealEstateApi.Application.DTOs;

/// <summary>LastPurchase*: when the owners bought and for how much, as they tell the agent (the
/// City's sales record only goes back a few years; older transfers are with the Deeds Office).
/// The rest: every figure the report pack uses (see ListingValuation), and the owners' bond.</summary>
public record UpsertValuationRequest(
    decimal? OwnersNetPrice,
    decimal? AgentValuation,
    decimal? CommissionPercent,
    DateTime? LastPurchaseDate = null,
    decimal? LastPurchasePriceZar = null,
    decimal? ValueLowZar = null,
    decimal? ValueHighZar = null,
    decimal? ListingPriceZar = null,
    string? AdjustmentReason = null,
    decimal? CommissionLatePercent = null,
    int? CommissionEarlyMonths = null,
    bool? CommissionIncludesVat = null,
    decimal? InterestRatePercent = null,
    int? BondTermYears = null,
    decimal? DepositPercent = null,
    string? BondInstitution = null,
    decimal? BondAmountZar = null
);

public record ValuationDto(
    int Id,
    decimal? OwnersNetPrice,
    decimal? AgentValuation,
    decimal? CommissionPercent,
    DateTime? LastPurchaseDate = null,
    decimal? LastPurchasePriceZar = null,
    decimal? ValueLowZar = null,
    decimal? ValueHighZar = null,
    decimal? ListingPriceZar = null,
    string? AdjustmentReason = null,
    decimal? CommissionLatePercent = null,
    int? CommissionEarlyMonths = null,
    bool? CommissionIncludesVat = null,
    decimal? InterestRatePercent = null,
    int? BondTermYears = null,
    decimal? DepositPercent = null,
    string? BondInstitution = null,
    decimal? BondAmountZar = null
);

public record UpsertRunningCostsRequest(
    decimal? MonthlyLevy,
    decimal? MonthlyRates,
    decimal? Electricity,
    decimal? Water,
    decimal? Sewage,
    decimal? Refuse
);

public record RunningCostsDto(
    int Id,
    int ListingId,
    decimal? MonthlyLevy,
    decimal? MonthlyRates,
    decimal? Electricity,
    decimal? Water,
    decimal? Sewage,
    decimal? Refuse
);
