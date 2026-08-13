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
        var roomsTask = BuildRoomDtosAsync(id);
        var parkingTask = _parkingRepo.GetByListingIdAsync(id);
        var contactsTask = _contactRepo.GetByListingIdAsync(id);
        var outdoorFeaturesTask = _outdoorFeatureRepo.GetByListingIdAsync(id);

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

    private async Task<List<RoomDto>> BuildRoomDtosAsync(int listingId)
    {
        var rooms = await _roomRepo.GetByListingIdAsync(listingId);
        var roomDtos = new List<RoomDto>();

        foreach (var room in rooms)
        {
            var conditionTask = _roomRepo.GetConditionByRoomIdAsync(room.Id);
            var featuresTask = _roomRepo.GetLinkedFeaturesAsync(room.Id);
            var customFeaturesTask = _roomRepo.GetCustomFeaturesAsync(room.Id);

            await Task.WhenAll(conditionTask, featuresTask, customFeaturesTask);

            roomDtos.Add(new RoomDto(
                room.Id, room.ListingId, room.Name, room.RoomTypeId,
                room.RoomTypeOther, room.PhotoUrl, room.CreatedAt, room.UpdatedAt,
                conditionTask.Result is null ? null : _mapper.Map<RoomConditionDto>(conditionTask.Result),
                _mapper.Map<List<FeatureDto>>(featuresTask.Result),
                _mapper.Map<List<CustomFeatureDto>>(customFeaturesTask.Result)
            ));
        }

        return roomDtos;
    }
}