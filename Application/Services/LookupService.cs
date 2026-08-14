using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class LookupService
{
    private readonly LookupRepository _lookupRepository;
    private readonly IMapper _mapper;

    public LookupService(LookupRepository lookupRepository, IMapper mapper)
    {
        _lookupRepository = lookupRepository;
        _mapper = mapper;
    }

    public async Task<IEnumerable<PropertyTypeDto>> GetPropertyTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = await _lookupRepository.GetPropertyTypesAsync(cancellationToken);
        return _mapper.Map<IEnumerable<PropertyTypeDto>>(types);
    }

    public async Task<IEnumerable<RoomTypeDto>> GetRoomTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = await _lookupRepository.GetRoomTypesAsync(cancellationToken);
        return _mapper.Map<IEnumerable<RoomTypeDto>>(types);
    }

    public async Task<IEnumerable<FeatureDto>> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        var features = await _lookupRepository.GetFeaturesAsync(cancellationToken);
        return _mapper.Map<IEnumerable<FeatureDto>>(features);
    }

    public async Task<IEnumerable<ConditionCategoryDto>> GetConditionCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _lookupRepository.GetConditionCategoriesAsync(cancellationToken);
        return _mapper.Map<IEnumerable<ConditionCategoryDto>>(categories);
    }

    public async Task<IEnumerable<ParkingTypeDto>> GetParkingTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = await _lookupRepository.GetParkingTypesAsync(cancellationToken);
        return _mapper.Map<IEnumerable<ParkingTypeDto>>(types);
    }

    public async Task<IEnumerable<FacingDto>> GetFacingAsync(CancellationToken cancellationToken = default)
    {
        var facing = await _lookupRepository.GetFacingAsync(cancellationToken);
        return _mapper.Map<IEnumerable<FacingDto>>(facing);
    }

    public async Task<IEnumerable<ZoningDto>> GetZoningAsync(CancellationToken cancellationToken = default)
    {
        var zoning = await _lookupRepository.GetZoningAsync(cancellationToken);
        return _mapper.Map<IEnumerable<ZoningDto>>(zoning);
    }
}