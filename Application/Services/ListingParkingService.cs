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

    public async Task<IEnumerable<ParkingDto>> GetParkingAsync(int listingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var parking = await _parkingRepo.GetByListingIdAsync(listingId, cancellationToken);
        return _mapper.Map<List<ParkingDto>>(parking);
    }

    public async Task<ParkingDto> AddParkingAsync(int listingId, AddParkingRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var parking = _mapper.Map<ListingParking>(request);
        parking.ListingId = listingId;

        var result = await _parkingRepo.CreateAsync(parking, cancellationToken);
        return _mapper.Map<ParkingDto>(result);
    }

    public async Task<ParkingDto?> UpdateParkingAsync(int listingId, int parkingId, UpdateParkingRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        await EnsureParkingBelongsToListingAsync(listingId, parkingId, cancellationToken);

        var result = await _parkingRepo.UpdateAsync(parkingId, request.Quantity, cancellationToken);
        if (result == null) return null;

        return _mapper.Map<ParkingDto>(result);
    }

    public async Task DeleteParkingAsync(int listingId, int parkingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

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