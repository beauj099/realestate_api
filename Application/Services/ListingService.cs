using AutoMapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class ListingService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingAddressRepository _addressRepo;
    private readonly ListingBuildingInfoRepository _buildingInfoRepo;
    private readonly ListingValuationRepository _valuationRepo;
    private readonly PropertyRunningCostsRepository _runningCostsRepo;
    private readonly ListingRoomRepository _roomRepo;
    private readonly ListingRoomPhotoRepository _roomPhotoRepo;
    private readonly ListingParkingRepository _parkingRepo;
    private readonly ContactRepository _contactRepo;
    private readonly ListingOutdoorFeatureRepository _outdoorFeatureRepo;
    private readonly ListingPhotoRepository _photoRepo;
    private readonly ListingDocumentRepository _documentRepo;
    private readonly IImageStorage _imageService;
    private readonly IOptions<R2Options> _r2Options;
    private readonly IMapper _mapper;

    public ListingService(
        ListingRepository listingRepo,
        ListingAddressRepository addressRepo,
        ListingBuildingInfoRepository buildingInfoRepo,
        ListingValuationRepository valuationRepo,
        PropertyRunningCostsRepository runningCostsRepo,
        ListingRoomRepository roomRepo,
        ListingRoomPhotoRepository roomPhotoRepo,
        ListingParkingRepository parkingRepo,
        ContactRepository contactRepo,
        ListingOutdoorFeatureRepository outdoorFeatureRepo,
        ListingPhotoRepository photoRepo,
        ListingDocumentRepository documentRepo,
        IImageStorage imageService,
        IOptions<R2Options> r2Options,
        IMapper mapper)
    {
        _listingRepo = listingRepo;
        _addressRepo = addressRepo;
        _buildingInfoRepo = buildingInfoRepo;
        _valuationRepo = valuationRepo;
        _runningCostsRepo = runningCostsRepo;
        _roomRepo = roomRepo;
        _roomPhotoRepo = roomPhotoRepo;
        _parkingRepo = parkingRepo;
        _contactRepo = contactRepo;
        _outdoorFeatureRepo = outdoorFeatureRepo;
        _photoRepo = photoRepo;
        _documentRepo = documentRepo;
        _imageService = imageService;
        _r2Options = r2Options;
        _mapper = mapper;
    }

    public async Task AssertOwnedAsync(int listingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
    }

    public async Task<ListingResponse> CreateAsync(CreateListingRequest request, CancellationToken cancellationToken = default)
    {
        return await CreateAsync(request, 0, false, cancellationToken);
    }

    public async Task<ListingResponse> CreateAsync(CreateListingRequest request, int userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.CreateAsync(request.PropertyTypeId, request.P24Ref, userId, cancellationToken);
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<ListingResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(id, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<ListingResponse?> GetByIdAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(id, userId, isAdmin, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<IEnumerable<ListingSummaryDto>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken = default)
    {
        var listings = await _listingRepo.GetAllAsync(status, dateFrom, dateTo, cancellationToken);
        return _mapper.Map<IEnumerable<ListingSummaryDto>>(listings);
    }

    public async Task<IEnumerable<ListingSummaryDto>> GetAllAsync(string? status, DateTime? dateFrom, DateTime? dateTo, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        return await _listingRepo.GetSummariesAsync(status, dateFrom, dateTo, userId, isAdmin, cancellationToken);
    }

    public async Task<ListingResponse?> UpdateAsync(int id, UpdateListingRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.UpdateAsync(id, request.Status, request.P24Ref, request.PropertyTypeId, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<ListingResponse?> UpdateAsync(int id, UpdateListingRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.UpdateAsync(id, request.Status, request.P24Ref, request.PropertyTypeId, userId, isAdmin, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _listingRepo.DeleteAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await AssertOwnedAsync(id, userId, isAdmin, cancellationToken);

        // Collect the listing's stored objects (listing photos, room photos and covers, and
        // documents) before the rows that reference them are gone. Works for both
        // the local "/uploads/..." URLs and the absolute R2 URLs.
        var photos = await _photoRepo.GetByListingIdAsync(id, cancellationToken);
        var rooms = await _roomRepo.GetByListingIdAsync(id, cancellationToken);
        var roomPhotos = await _roomPhotoRepo.GetByListingIdAsync(id, cancellationToken);
        var documentKeys = await GetDocumentStorageKeysAsync(id, cancellationToken);
        var keys = photos.Select(p => p.Url)
            .Concat(rooms.Where(r => r.PhotoUrl is not null).Select(r => r.PhotoUrl!))
            .Select(ExtractKeyFromUrl)
            // Room photo rows go with their rooms via ON DELETE CASCADE. A NULL key is a
            // backfilled legacy URL outside our storage layout, so there is nothing to delete.
            .Concat(roomPhotos.Where(p => p.StorageKey is not null).Select(p => p.StorageKey!))
            .Concat(documentKeys)
            // Only objects stored under this listing; a stray URL must never let a
            // delete reach another listing's files.
            .Where(k => k.StartsWith($"listings/{id}/") || k.StartsWith($"rooms/{id}/"))
            .Distinct()
            .ToList();

        // Database first: if it fails, the listing still has all its photos.
        await _listingRepo.DeleteAsync(id, userId, isAdmin, cancellationToken);

        // Then best-effort storage cleanup so orphaned files do not pile up.
        foreach (var key in keys)
        {
            try { await _imageService.DeleteAsync(key); }
            catch { /* An orphaned object is harmless; the listing is already gone. */ }
        }
    }

    /// <summary>
    /// Document rows themselves go with the listing via ON DELETE CASCADE. Until the
    /// ListingDocuments patch has been applied the table does not exist (SQL error 208);
    /// listing delete must keep working in that case, so there is simply nothing to clean.
    /// </summary>
    private async Task<IEnumerable<string>> GetDocumentStorageKeysAsync(int listingId, CancellationToken cancellationToken)
    {
        try
        {
            var documents = await _documentRepo.GetByListingIdAsync(listingId, cancellationToken);
            return documents.Select(d => d.StorageKey).ToList();
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return [];
        }
    }

    private string ExtractKeyFromUrl(string url)
    {
        const string localPrefix = "/uploads/";
        if (url.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
            return url[localPrefix.Length..];
        var prefix = _r2Options.Value.PublicUrl.TrimEnd('/') + "/";
        return url.StartsWith(prefix) ? url[prefix.Length..] : url;
    }

    /// <summary>Stores the house score; 404 (KeyNotFoundException) when the listing is not the caller's.</summary>
    public async Task UpdateHouseScoreAsync(int id, UpdateHouseScoreRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        // Two decimals would be silently rounded by DECIMAL(4,1); round explicitly instead.
        var score = request.Score is { } s ? Math.Round(s, 1, MidpointRounding.AwayFromZero) : (decimal?)null;
        if (!await _listingRepo.UpdateHouseScoreAsync(id, score, request.IsManual, userId, isAdmin, cancellationToken))
            throw new KeyNotFoundException($"Listing {id} not found");
    }

    /// <summary>Archives or restores a listing; 404 (KeyNotFoundException) when the listing is not the caller's.</summary>
    public async Task SetArchivedAsync(int id, ArchiveListingRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (!await _listingRepo.SetArchivedAsync(id, request.Archived, userId, isAdmin, cancellationToken))
            throw new KeyNotFoundException($"Listing {id} not found");
    }

    public async Task<ListingResponse?> SubmitAsync(int id, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.SubmitAsync(id, cancellationToken);
        if (listing == null) return null;
        return await BuildFullResponseAsync(listing, cancellationToken);
    }

    public async Task<ListingResponse?> SubmitAsync(int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.SubmitAsync(id, userId, isAdmin, cancellationToken);
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
        var roomsTask = RoomDtoBuilder.BuildAsync(_roomRepo, _roomPhotoRepo, _mapper, id, cancellationToken);
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
            _mapper.Map<List<OutdoorFeatureDto>>(outdoorFeaturesTask.Result),
            listing.HouseScore,
            listing.HouseScoreIsManual,
            listing.ArchivedAt
        );
    }
}