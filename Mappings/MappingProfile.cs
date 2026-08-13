using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;

namespace RealEstateApi.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Lookup entities -> DTOs
        CreateMap<PropertyType, PropertyTypeDto>();
        CreateMap<RoomType, RoomTypeDto>();
        CreateMap<Feature, FeatureDto>();
        CreateMap<ConditionCategory, ConditionCategoryDto>();
        CreateMap<ParkingType, ParkingTypeDto>();
        CreateMap<Facing, FacingDto>();
        CreateMap<Zoning, ZoningDto>();

        // Listing entities -> DTOs
        CreateMap<Listing, ListingSummaryDto>();

        CreateMap<ListingAddress, ListingAddressDto>();

        CreateMap<ListingBuildingInfo, BuildingInfoDto>();

        CreateMap<ListingValuation, ValuationDto>();

        CreateMap<PropertyRunningCosts, RunningCostsDto>();

        CreateMap<ListingParking, ParkingDto>()
            .ForMember(dest => dest.ParkingTypeDescription,
                opt => opt.MapFrom(src => src.ParkingTypeDescription ?? ""));

        CreateMap<Condition, RoomConditionDto>();

        CreateMap<ListingRoomCustomFeature, CustomFeatureDto>();

        CreateMap<ListingOutdoorFeature, OutdoorFeatureDto>();

        CreateMap<Contact, ContactDto>();

        // Request -> Entity mappings (inbound)
        CreateMap<UpsertAddressRequest, ListingAddress>();
        CreateMap<UpsertBuildingInfoRequest, ListingBuildingInfo>();
        CreateMap<UpsertValuationRequest, ListingValuation>();
        CreateMap<UpsertRunningCostsRequest, PropertyRunningCosts>();
        CreateMap<CreateRoomRequest, ListingRoom>();
        CreateMap<UpsertRoomConditionRequest, Condition>();
        CreateMap<AddCustomFeatureRequest, ListingRoomCustomFeature>();
        CreateMap<AddParkingRequest, ListingParking>();
        CreateMap<AddOutdoorFeatureRequest, ListingOutdoorFeature>();

        CreateMap<AddContactRequest, Contact>();

        CreateMap<UpdateRoomRequest, ListingRoom>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name ?? string.Empty))
            .ForMember(dest => dest.RoomTypeId, opt => opt.MapFrom(src => src.RoomTypeId ?? 0));

        CreateMap<UpdateContactRequest, Contact>();
    }
}