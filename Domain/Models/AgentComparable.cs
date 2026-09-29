namespace RealEstateApi.Domain.Models;

/// <summary>A sale an agent knows about, captured in the app (dbo.AgentComparables).</summary>
public class AgentComparable
{
    public Guid Id { get; set; }
    public int CapturedByUserId { get; set; }
    public DateTime CapturedAt { get; set; }

    public string Municipality { get; set; } = string.Empty;
    public string Suburb { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Erf { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public decimal? ErfM2 { get; set; }
    public decimal? FloorM2 { get; set; }
    public byte? Bedrooms { get; set; }
    public byte? Bathrooms { get; set; }
    public byte? Garages { get; set; }
    public bool? HasPool { get; set; }
    public int? PropertyTypeId { get; set; }
    public string? Condition { get; set; }

    public DateTime SaleDate { get; set; }
    public decimal SalePriceZar { get; set; }
    public string EvidenceLevel { get; set; } = "Hearsay";
    public string? Notes { get; set; }
    public int CorroborationCount { get; set; }

    public string Verification { get; set; } = "Unverified";
    public string? VerifiedAgainst { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public decimal? PricePerFloorM2 { get; set; }
    public decimal Weight { get; set; }
}
