namespace RealEstateApi.Domain.Models;

public class Contact
{
    public int Id { get; set; }
    public string? FullName { get; set; }
    public string? IdNumber { get; set; }
    public string? CompanyName { get; set; }
    public string? CompanyRegistrationNumber { get; set; }
    public string? MobilePhone { get; set; }
    public string? EmailAddress { get; set; }
    public string? Role { get; set; }
    public string? OwnerType { get; set; } // 'Person' | 'Business'
    public int ListingId { get; set; }
}
