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

    public async Task<IEnumerable<PropertyTypeDto>> GetPropertyTypesAsync()
    {
        var types = await _lookupRepository.GetPropertyTypesAsync();
        return _mapper.Map<IEnumerable<PropertyTypeDto>>(types);
    }

    public async Task<IEnumerable<RoomTypeDto>> GetRoomTypesAsync()
    {
        var types = await _lookupRepository.GetRoomTypesAsync();
        return _mapper.Map<IEnumerable<RoomTypeDto>>(types);
    }

    public async Task<IEnumerable<FeatureDto>> GetFeaturesAsync()
    {
        var features = await _lookupRepository.GetFeaturesAsync();
        return _mapper.Map<IEnumerable<FeatureDto>>(features);
    }

    public async Task<IEnumerable<ConditionCategoryDto>> GetConditionCategoriesAsync()
    {
        var categories = await _lookupRepository.GetConditionCategoriesAsync();
        return _mapper.Map<IEnumerable<ConditionCategoryDto>>(categories);
    }

    public async Task<IEnumerable<ParkingTypeDto>> GetParkingTypesAsync()
    {
        var types = await _lookupRepository.GetParkingTypesAsync();
        return _mapper.Map<IEnumerable<ParkingTypeDto>>(types);
    }

    public async Task<IEnumerable<FacingDto>> GetFacingAsync()
    {
        var facing = await _lookupRepository.GetFacingAsync();
        return _mapper.Map<IEnumerable<FacingDto>>(facing);
    }

    public async Task<IEnumerable<ZoningDto>> GetZoningAsync()
    {
        var zoning = await _lookupRepository.GetZoningAsync();
        return _mapper.Map<IEnumerable<ZoningDto>>(zoning);
    }
}
