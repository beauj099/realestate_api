using RealEstateApi.Application.Interfaces;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

public class ContactRepository : DapperRepository, IContactRepository
{
    public ContactRepository(DbConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public Task<IEnumerable<Contact>> GetByListingIdAsync(int listingId) =>
        QueryProcAsync<Contact>("sp_Contacts_GetByListingId", new { ListingId = listingId });

    public Task<Contact> CreateAsync(Contact contact) =>
        QuerySingleProcAsync<Contact>(
            "sp_Contacts_Create",
            new
            {
                contact.ListingId,
                contact.FullName,
                contact.IdNumber,
                contact.CompanyName,
                contact.CompanyRegistrationNumber,
                contact.MobilePhone,
                contact.EmailAddress,
                contact.Role
            });

    public Task<Contact?> UpdateAsync(Contact contact) =>
        QuerySingleOrDefaultProcAsync<Contact>(
            "sp_Contacts_Update",
            new
            {
                contact.Id,
                contact.FullName,
                contact.IdNumber,
                contact.CompanyName,
                contact.CompanyRegistrationNumber,
                contact.MobilePhone,
                contact.EmailAddress,
                contact.Role
            });

    public Task DeleteAsync(int id) =>
        ExecuteProcAsync("sp_Contacts_Delete", new { Id = id });
}
