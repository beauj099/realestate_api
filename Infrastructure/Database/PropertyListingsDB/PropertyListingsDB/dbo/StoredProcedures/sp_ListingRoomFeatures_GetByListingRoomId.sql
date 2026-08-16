
-- Room Features (junction)
CREATE   PROCEDURE sp_ListingRoomFeatures_GetByListingRoomId
    @ListingRoomId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT f.Id, f.Category, f.Description
    FROM ListingRoomFeature lrf
    INNER JOIN Feature f ON f.Id = lrf.FeatureId
    WHERE lrf.ListingRoomId = @ListingRoomId;
END

GO

