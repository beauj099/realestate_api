namespace RealEstateApi.Application.DTOs;

public record AddContactRequest(
    string? FullName,
    string? IdNumber,
    string? CompanyName,
    string? CompanyRegistrationNumber,
    string? MobilePhone,
    string? EmailAddress,
    string? Role,
    string? OwnerType
);

public record UpdateContactRequest(
    string? FullName,
    string? IdNumber,
    string? CompanyName,
    string? CompanyRegistrationNumber,
    string? MobilePhone,
    string? EmailAddress,
    string? Role,
    string? OwnerType
);

public record ContactDto(
    int Id,
    string? FullName,
    string? IdNumber,
    string? CompanyName,
    string? CompanyRegistrationNumber,
    string? MobilePhone,
    string? EmailAddress,
    string? Role,
    string? OwnerType,
    int ListingId
);
