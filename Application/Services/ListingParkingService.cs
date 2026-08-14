using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class ListingParkingService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingParkingRepository _parkingRepo;
    private readonly IMapper _mapper;

    public ListingParkingService(ListingRepository listingRepo, ListingParkingRepository parkingRepo, IMapper mapper)
    {
        _listingRepo = listingRepo;
        _parkingRepo = parkingRepo;
        _mapper = mapper;
    }

    public async Task<IEnumerable<ParkingDto>> GetParkingAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var parking = await _parkingRepo.GetByListingIdAsync(listingId, cancellationToken);
        return _mapper.Map<List<ParkingDto>>(parking);
    }

    public async Task<ParkingDto> AddParkingAsync(int listingId, AddParkingRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var parking = _mapper.Map<ListingParking>(request);
        parking.ListingId = listingId;

        var result = await _parkingRepo.CreateAsync(parking, cancellationToken);
        return _mapper.Map<ParkingDto>(result);
    }

    public async Task<ParkingDto?> UpdateParkingAsync(int listingId, int parkingId, UpdateParkingRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await EnsureParkingBelongsToListingAsync(listingId, parkingId, cancellationToken);

        var result = await _parkingRepo.UpdateAsync(parkingId, request.Quantity, cancellationToken);
        if (result == null) return null;

        return _mapper.Map<ParkingDto>(result);
    }

    public async Task DeleteParkingAsync(int listingId, int parkingId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await EnsureParkingBelongsToListingAsync(listingId, parkingId, cancellationToken);

        await _parkingRepo.DeleteAsync(parkingId, cancellationToken);
    }

    private async Task EnsureParkingBelongsToListingAsync(int listingId, int parkingId, CancellationToken cancellationToken)
    {
        var parking = await _parkingRepo.GetByIdAsync(parkingId, cancellationToken);
        if (parking is null || parking.ListingId != listingId)
            throw new KeyNotFoundException($"Parking {parkingId} not found under listing {listingId}");
    }
}