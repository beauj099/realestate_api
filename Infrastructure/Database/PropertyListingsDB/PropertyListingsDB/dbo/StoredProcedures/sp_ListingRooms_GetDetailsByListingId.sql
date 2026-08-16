
-- Returns everything needed to build the room DTOs for a listing in ONE round trip:
-- (1) rooms, (2) their conditions, (3) their linked features, (4) their custom features.
-- Replaces the previous 1 + 3N per-room query pattern.
CREATE   PROCEDURE sp_ListingRooms_GetDetailsByListingId
    @ListingId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1: rooms
    SELECT Id, ListingId, Name, RoomTypeId, RoomTypeOther, PhotoUrl, CreatedAt, UpdatedAt
    FROM ListingRoom
    WHERE ListingId = @ListingId
    ORDER BY CreatedAt;

    -- 2: conditions for those rooms
    SELECT c.Id, c.ListingRoomId, c.ConditionRating, c.Notes, c.ConditionCategoryId
    FROM Condition c
    INNER JOIN ListingRoom r ON r.Id = c.ListingRoomId
    WHERE r.ListingId = @ListingId;

    -- 3: linked features (carrying ListingRoomId so they can be grouped per room)
    SELECT lrf.ListingRoomId, f.Id, f.Category, f.Description
    FROM ListingRoomFeature lrf
    INNER JOIN ListingRoom r ON r.Id = lrf.ListingRoomId
    INNER JOIN Feature f ON f.Id = lrf.FeatureId
    WHERE r.ListingId = @ListingId;

    -- 4: custom features
    SELECT cf.Id, cf.ListingRoomId, cf.Description
    FROM ListingRoomCustomFeature cf
    INNER JOIN ListingRoom r ON r.Id = cf.ListingRoomId
    WHERE r.ListingId = @ListingId;
END

GO
