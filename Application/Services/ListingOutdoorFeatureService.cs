using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class ListingOutdoorFeatureService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingOutdoorFeatureRepository _outdoorFeatureRepo;
    private readonly IMapper _mapper;

    public ListingOutdoorFeatureService(ListingRepository listingRepo, ListingOutdoorFeatureRepository outdoorFeatureRepo, IMapper mapper)
    {
        _listingRepo = listingRepo;
        _outdoorFeatureRepo = outdoorFeatureRepo;
        _mapper = mapper;
    }

    public async Task<IEnumerable<OutdoorFeatureDto>> GetByListingIdAsync(int listingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var features = await _outdoorFeatureRepo.GetByListingIdAsync(listingId, cancellationToken);
        return _mapper.Map<List<OutdoorFeatureDto>>(features);
    }

    public async Task<OutdoorFeatureDto> AddAsync(int listingId, AddOutdoorFeatureRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var feature = _mapper.Map<ListingOutdoorFeature>(request);
        feature.ListingId = listingId;

        var result = await _outdoorFeatureRepo.AddAsync(feature, cancellationToken);
        return _mapper.Map<OutdoorFeatureDto>(result);
    }

    public async Task DeleteAsync(int listingId, int id, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var feature = await _outdoorFeatureRepo.GetByIdAsync(id, cancellationToken);
        if (feature is null || feature.ListingId != listingId)
            throw new KeyNotFoundException($"Outdoor feature {id} not found under listing {listingId}");

        await _outdoorFeatureRepo.DeleteAsync(id, cancellationToken);
    }

    public async Task<IEnumerable<OutdoorFeatureDto>> ReplaceAllAsync(int listingId, ReplaceOutdoorFeaturesRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var result = await _outdoorFeatureRepo.ReplaceAllAsync(listingId, request.Descriptions, cancellationToken);
        return _mapper.Map<List<OutdoorFeatureDto>>(result);
    }
}