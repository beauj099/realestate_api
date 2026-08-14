using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class ListingContactService
{
    private readonly ListingRepository _listingRepo;
    private readonly ContactRepository _contactRepo;
    private readonly IMapper _mapper;

    public ListingContactService(ListingRepository listingRepo, ContactRepository contactRepo, IMapper mapper)
    {
        _listingRepo = listingRepo;
        _contactRepo = contactRepo;
        _mapper = mapper;
    }

    public async Task<IEnumerable<ContactDto>> GetContactsAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var contacts = await _contactRepo.GetByListingIdAsync(listingId, cancellationToken);
        return _mapper.Map<List<ContactDto>>(contacts);
    }

    public async Task<ContactDto> AddContactAsync(int listingId, AddContactRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var contact = _mapper.Map<Contact>(request);
        contact.ListingId = listingId;

        var result = await _contactRepo.CreateAsync(contact, cancellationToken);
        return _mapper.Map<ContactDto>(result);
    }

    public async Task<ContactDto?> UpdateContactAsync(int listingId, int contactId, UpdateContactRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await EnsureContactBelongsToListingAsync(listingId, contactId, cancellationToken);

        var contact = _mapper.Map<Contact>(request);
        contact.Id = contactId;
        contact.ListingId = listingId;

        var result = await _contactRepo.UpdateAsync(contact, cancellationToken);
        return result is null ? null : _mapper.Map<ContactDto>(result);
    }

    public async Task DeleteContactAsync(int listingId, int contactId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await EnsureContactBelongsToListingAsync(listingId, contactId, cancellationToken);

        await _contactRepo.DeleteAsync(contactId, cancellationToken);
    }

    private async Task EnsureContactBelongsToListingAsync(int listingId, int contactId, CancellationToken cancellationToken)
    {
        var contact = await _contactRepo.GetByIdAsync(contactId, cancellationToken);
        if (contact is null || contact.ListingId != listingId)
            throw new KeyNotFoundException($"Contact {contactId} not found under listing {listingId}");
    }
}