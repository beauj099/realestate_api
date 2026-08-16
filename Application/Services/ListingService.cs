using AutoMapper;
using RealEstateApi.Application.Interfaces;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;

namespace RealEstateApi.Application.Services;

public class ListingService : IListingService
{
    private readonly IListingRepository _listingRepo;
    private readonly IListingAddressRepository _addressRepo;
    private readonly IListingBuildingInfoRepository _buildingInfoRepo;
    private readonly IListingValuationRepository _valuationRepo;
    private readonly IPropertyRunningCostsRepository _runningCostsRepo;
    private readonly IRoomAssembler _roomAssembler;
    private readonly IListingParkingRepository _parkingRepo;
    private readonly IContactRepository _contactRepo;
    private readonly IMapper _mapper;

    public ListingService(
        IListingRepository listingRepo,
        IListingAddressRepository addressRepo,
        IListingBuildingInfoRepository buildingInfoRepo,
        IListingValuationRepository valuationRepo,
        IPropertyRunningCostsRepository runningCostsRepo,
        IRoomAssembler roomAssembler,
        IListingParkingRepository parkingRepo,
        IContactRepository contactRepo,
        IMapper mapper)
    {
        _listingRepo = listingRepo;
        _addressRepo = addressRepo;
        _buildingInfoRepo = buildingInfoRepo;
        _valuationRepo = valuationRepo;
        _runningCostsRepo = runningCostsRepo;
        _roomAssembler = roomAssembler;
        _parkingRepo = parkingRepo;
        _contactRepo = contactRepo;
        _mapper = mapper;
    }

    public async Task<ListingResponse> CreateAsync(CreateListingRequest request)
    {
        var listing = await _listingRepo.CreateAsync(request.PropertyTypeId, request.P24Ref);
        return await BuildFullResponseAsync(listing);
    }

    public async Task<ListingResponse?> GetByIdAsync(int id)
    {
        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing);
    }

    public async Task<IEnumerable<ListingSummaryDto>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo)
    {
        var listings = await _listingRepo.GetAllAsync(status, dateFrom, dateTo);
        return _mapper.Map<IEnumerable<ListingSummaryDto>>(listings);
    }

    public async Task<ListingResponse?> UpdateAsync(int id, UpdateListingRequest request)
    {
        var listing = await _listingRepo.UpdateAsync(id, request.Status, request.P24Ref, request.PropertyTypeId);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing);
    }

    public async Task DeleteAsync(int id)
    {
        await _listingRepo.DeleteAsync(id);
    }

    public async Task<ListingResponse?> SubmitAsync(int id)
    {
        var listing = await _listingRepo.SubmitAsync(id);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing);
    }

    public async Task<ListingAddressDto?> GetAddressAsync(int listingId)
    {
        var address = await _addressRepo.GetByListingIdAsync(listingId);
        return address is null ? null : _mapper.Map<ListingAddressDto>(address);
    }

    public async Task<ListingAddressDto> UpsertAddressAsync(int listingId, UpsertAddressRequest request)
    {
        var address = _mapper.Map<ListingAddress>(request);
        address.ListingId = listingId;
        var result = await _addressRepo.UpsertAsync(address);
        return _mapper.Map<ListingAddressDto>(result);
    }

    public async Task<BuildingInfoDto?> GetBuildingInfoAsync(int listingId)
    {
        var info = await _buildingInfoRepo.GetByListingIdAsync(listingId);
        return info is null ? null : _mapper.Map<BuildingInfoDto>(info);
    }

    public async Task<BuildingInfoDto> UpsertBuildingInfoAsync(int listingId, UpsertBuildingInfoRequest request)
    {
        var info = _mapper.Map<ListingBuildingInfo>(request);
        info.ListingId = listingId;
        var result = await _buildingInfoRepo.UpsertAsync(info);
        return _mapper.Map<BuildingInfoDto>(result);
    }

    public async Task<ValuationDto?> GetValuationAsync(int listingId)
    {
        var valuation = await _valuationRepo.GetByListingIdAsync(listingId);
        return valuation is null ? null : _mapper.Map<ValuationDto>(valuation);
    }

    public async Task<ValuationDto> UpsertValuationAsync(int listingId, UpsertValuationRequest request)
    {
        var valuation = _mapper.Map<ListingValuation>(request);
        var result = await _valuationRepo.UpsertAsync(listingId, valuation);
        return _mapper.Map<ValuationDto>(result);
    }

    public async Task<RunningCostsDto?> GetRunningCostsAsync(int listingId)
    {
        var costs = await _runningCostsRepo.GetByListingIdAsync(listingId);
        return costs is null ? null : _mapper.Map<RunningCostsDto>(costs);
    }

    public async Task<RunningCostsDto> UpsertRunningCostsAsync(int listingId, UpsertRunningCostsRequest request)
    {
        var costs = _mapper.Map<PropertyRunningCosts>(request);
        costs.ListingId = listingId;
        var result = await _runningCostsRepo.UpsertAsync(costs);
        return _mapper.Map<RunningCostsDto>(result);
    }

    private async Task<ListingResponse> BuildFullResponseAsync(Listing listing)
    {
        var id = listing.Id;

        var addressTask = _addressRepo.GetByListingIdAsync(id);
        var buildingInfoTask = _buildingInfoRepo.GetByListingIdAsync(id);
        var valuationTask = _valuationRepo.GetByListingIdAsync(id);
        var runningCostsTask = _runningCostsRepo.GetByListingIdAsync(id);
        var roomsTask = _roomAssembler.GetRoomDtosAsync(id);
        var parkingTask = _parkingRepo.GetByListingIdAsync(id);
        var contactsTask = _contactRepo.GetByListingIdAsync(id);

        await Task.WhenAll(addressTask, buildingInfoTask, valuationTask, runningCostsTask, roomsTask, parkingTask, contactsTask);

        return new ListingResponse(
            listing.Id, listing.ReferenceNumber, listing.P24Ref, listing.PropertyTypeId,
            listing.ListingValuationId, listing.ListDate, listing.Status,
            listing.CreatedAt, listing.UpdatedAt,
            addressTask.Result is null ? null : _mapper.Map<ListingAddressDto>(addressTask.Result),
            buildingInfoTask.Result is null ? null : _mapper.Map<BuildingInfoDto>(buildingInfoTask.Result),
            valuationTask.Result is null ? null : _mapper.Map<ValuationDto>(valuationTask.Result),
            runningCostsTask.Result is null ? null : _mapper.Map<RunningCostsDto>(runningCostsTask.Result),
            roomsTask.Result,
            parkingTask.Result.Select(p => _mapper.Map<ParkingDto>(p)).ToList(),
            contactsTask.Result.Select(c => _mapper.Map<ContactDto>(c)).ToList()
        );
    }
}
