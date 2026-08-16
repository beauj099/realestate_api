using RealEstateApi.Application.DTOs;

namespace RealEstateApi.Application.Interfaces;

/// <summary>
/// Builds fully-populated room DTOs (rooms + condition + features + custom features)
/// for a listing. Single source of truth shared by the listing and room services.
/// </summary>
public interface IRoomAssembler
{
    Task<List<RoomDto>> GetRoomDtosAsync(int listingId);
}
