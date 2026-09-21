using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;

namespace RealEstateApi.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Lookup entities -> DTOs
        CreateMap<PropertyType, PropertyTypeDto>().ReverseMap();
        CreateMap<RoomType, RoomTypeDto>().ReverseMap();
        CreateMap<Feature, FeatureDto>().ReverseMap();
        CreateMap<ConditionCategory, ConditionCategoryDto>().ReverseMap();
        CreateMap<ParkingType, ParkingTypeDto>().ReverseMap();
        CreateMap<Facing, FacingDto>().ReverseMap();
        CreateMap<Zoning, ZoningDto>().ReverseMap();

        // Listing entities -> DTOs
        CreateMap<Listing, ListingSummaryDto>().ReverseMap();

        CreateMap<ListingAddress, ListingAddressDto>().ReverseMap();

        CreateMap<ListingBuildingInfo, BuildingInfoDto>().ReverseMap();

        CreateMap<ListingValuation, ValuationDto>().ReverseMap();

        CreateMap<PropertyRunningCosts, RunningCostsDto>().ReverseMap();

        CreateMap<ListingParking, ParkingDto>()
            .ForMember(dest => dest.ParkingTypeDescription,
                opt => opt.MapFrom(src => src.ParkingTypeDescription ?? ""));

        CreateMap<Condition, RoomConditionDto>().ReverseMap();

        CreateMap<ListingRoomCustomFeature, CustomFeatureDto>().ReverseMap();

        CreateMap<ListingOutdoorFeature, OutdoorFeatureDto>().ReverseMap();

        CreateMap<Contact, ContactDto>().ReverseMap();

        // Request -> Entity mappings (inbound)
        CreateMap<UpsertAddressRequest, ListingAddress>().ReverseMap();
        CreateMap<UpsertBuildingInfoRequest, ListingBuildingInfo>().ReverseMap();
        CreateMap<UpsertValuationRequest, ListingValuation>().ReverseMap();
        CreateMap<UpsertRunningCostsRequest, PropertyRunningCosts>().ReverseMap();
        CreateMap<CreateRoomRequest, ListingRoom>().ReverseMap();
        CreateMap<UpsertRoomConditionRequest, Condition>().ReverseMap();
        CreateMap<AddCustomFeatureRequest, ListingRoomCustomFeature>().ReverseMap();
        CreateMap<AddParkingRequest, ListingParking>().ReverseMap();
        CreateMap<AddOutdoorFeatureRequest, ListingOutdoorFeature>().ReverseMap();

        CreateMap<AddContactRequest, Contact>().ReverseMap();

        CreateMap<UpdateContactRequest, Contact>().ReverseMap();
    }
}