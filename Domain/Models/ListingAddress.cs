namespace RealEstateApi.Domain.Models;

public class ListingAddress
{
    public int ListingAddressId { get; set; }
    public int ListingId { get; set; }
    public string? ErfNumber { get; set; }
    public string? EstateName { get; set; }
    public string? StreetNumber { get; set; }
    public string? UnitNumber { get; set; }
    public string? Street { get; set; }
    public string? Suburb { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    /// <summary>The area buyers search for it by, Property24's name (Steynsrust), when it differs
    /// from the official Suburb (Lynn's View) the records use. Null: not set.</summary>
    public string? MarketingArea { get; set; }
}
