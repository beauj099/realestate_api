using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class ListingService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingAddressRepository _addressRepo;
    private readonly ListingBuildingInfoRepository _buildingInfoRepo;
    private readonly ListingValuationRepository _valuationRepo;
    private readonly PropertyRunningCostsRepository _runningCostsRepo;
    private readonly ListingRoomRepository _roomRepo;
    private readonly ListingParkingRepository _parkingRepo;
    private readonly ContactRepository _contactRepo;
    private readonly ListingOutdoorFeatureRepository _outdoorFeatureRepo;
    private readonly IMapper _mapper;

    public ListingService(
        ListingRepository listingRepo,
        ListingAddressRepository addressRepo,
        ListingBuildingInfoRepository buildingInfoRepo,
        ListingValuationRepository valuationRepo,
        PropertyRunningCostsRepository runningCostsRepo,
        ListingRoomRepository roomRepo,
        ListingParkingRepository parkingRepo,
        ContactRepository contactRepo,
        ListingOutdoorFeatureRepository outdoorFeatureRepo,
        IMapper mapper)
    {
        _listingRepo = listingRepo;
        _addressRepo = addressRepo;
        _buildingInfoRepo = buildingInfoRepo;
        _valuationRepo = valuationRepo;
        _runningCostsRepo = runningCostsRepo;
        _roomRepo = roomRepo;
        _parkingRepo = parkingRepo;
        _contactRepo = contactRepo;
        _outdoorFeatureRepo = outdoorFeatureRepo;
        _mapper = mapper;
    }

    public async Task<ListingResponse> CreateAsync(CreateListingRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.CreateAsync(request.PropertyTypeId, request.P24Ref, cancellationToken);
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<ListingResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(id, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<IEnumerable<ListingSummaryDto>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken = default)
    {
        var listings = await _listingRepo.GetAllAsync(status, dateFrom, dateTo, cancellationToken);
        return _mapper.Map<IEnumerable<ListingSummaryDto>>(listings);
    }

    public async Task<ListingResponse?> UpdateAsync(int id, UpdateListingRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.UpdateAsync(id, request.Status, request.P24Ref, request.PropertyTypeId, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _listingRepo.DeleteAsync(id, cancellationToken);
    }

    public async Task<ListingResponse?> SubmitAsync(int id, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.SubmitAsync(id, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<ListingAddressDto?> GetAddressAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var address = await _addressRepo.GetByListingIdAsync(listingId, cancellationToken);
        return address is null ? null : _mapper.Map<ListingAddressDto>(address);
    }

    public async Task<ListingAddressDto> UpsertAddressAsync(int listingId, UpsertAddressRequest request, CancellationToken cancellationToken = default)
    {
        var address = _mapper.Map<ListingAddress>(request);
        address.ListingId = listingId;
        var result = await _addressRepo.UpsertAsync(address, cancellationToken);
        return _mapper.Map<ListingAddressDto>(result);
    }

    public async Task<BuildingInfoDto?> GetBuildingInfoAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var info = await _buildingInfoRepo.GetByListingIdAsync(listingId, cancellationToken);
        return info is null ? null : _mapper.Map<BuildingInfoDto>(info);
    }

    public async Task<BuildingInfoDto> UpsertBuildingInfoAsync(int listingId, UpsertBuildingInfoRequest request, CancellationToken cancellationToken = default)
    {
        var info = _mapper.Map<ListingBuildingInfo>(request);
        info.ListingId = listingId;
        var result = await _buildingInfoRepo.UpsertAsync(info, cancellationToken);
        return _mapper.Map<BuildingInfoDto>(result);
    }

    public async Task<ValuationDto?> GetValuationAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var valuation = await _valuationRepo.GetByListingIdAsync(listingId, cancellationToken);
        return valuation is null ? null : _mapper.Map<ValuationDto>(valuation);
    }

    public async Task<ValuationDto> UpsertValuationAsync(int listingId, UpsertValuationRequest request, CancellationToken cancellationToken = default)
    {
        var valuation = _mapper.Map<ListingValuation>(request);
        var result = await _valuationRepo.UpsertAsync(listingId, valuation, cancellationToken);
        return _mapper.Map<ValuationDto>(result);
    }

    public async Task<RunningCostsDto?> GetRunningCostsAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var costs = await _runningCostsRepo.GetByListingIdAsync(listingId, cancellationToken);
        return costs is null ? null : _mapper.Map<RunningCostsDto>(costs);
    }

    public async Task<RunningCostsDto> UpsertRunningCostsAsync(int listingId, UpsertRunningCostsRequest request, CancellationToken cancellationToken = default)
    {
        var costs = _mapper.Map<PropertyRunningCosts>(request);
        costs.ListingId = listingId;
        var result = await _runningCostsRepo.UpsertAsync(costs, cancellationToken);
        return _mapper.Map<RunningCostsDto>(result);
    }

    private async Task<ListingResponse> BuildFullResponseAsync(Listing listing, CancellationToken cancellationToken)
    {
        var id = listing.Id;

        var addressTask = _addressRepo.GetByListingIdAsync(id, cancellationToken);
        var buildingInfoTask = _buildingInfoRepo.GetByListingIdAsync(id, cancellationToken);
        var valuationTask = _valuationRepo.GetByListingIdAsync(id, cancellationToken);
        var runningCostsTask = _runningCostsRepo.GetByListingIdAsync(id, cancellationToken);
        var roomsTask = RoomDtoBuilder.BuildAsync(_roomRepo, _mapper, id, cancellationToken);
        var parkingTask = _parkingRepo.GetByListingIdAsync(id, cancellationToken);
        var contactsTask = _contactRepo.GetByListingIdAsync(id, cancellationToken);
        var outdoorFeaturesTask = _outdoorFeatureRepo.GetByListingIdAsync(id, cancellationToken);

        await Task.WhenAll(addressTask, buildingInfoTask, valuationTask, runningCostsTask, roomsTask, parkingTask, contactsTask, outdoorFeaturesTask);

        return new ListingResponse(
            listing.Id, listing.ReferenceNumber, listing.P24Ref, listing.PropertyTypeId,
            listing.ListingValuationId, listing.ListDate, listing.Status,
            listing.CreatedAt, listing.UpdatedAt,
            addressTask.Result is null ? null : _mapper.Map<ListingAddressDto>(addressTask.Result),
            buildingInfoTask.Result is null ? null : _mapper.Map<BuildingInfoDto>(buildingInfoTask.Result),
            valuationTask.Result is null ? null : _mapper.Map<ValuationDto>(valuationTask.Result),
            runningCostsTask.Result is null ? null : _mapper.Map<RunningCostsDto>(runningCostsTask.Result),
            roomsTask.Result,
            _mapper.Map<List<ParkingDto>>(parkingTask.Result),
            _mapper.Map<List<ContactDto>>(contactsTask.Result),
            _mapper.Map<List<OutdoorFeatureDto>>(outdoorFeaturesTask.Result)
        );
    }
}