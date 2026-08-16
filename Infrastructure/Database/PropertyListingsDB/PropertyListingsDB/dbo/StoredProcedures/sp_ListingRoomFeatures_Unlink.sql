
CREATE   PROCEDURE sp_ListingRoomFeatures_Unlink
    @ListingRoomId INT,
    @FeatureId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM ListingRoomFeature
    WHERE ListingRoomId = @ListingRoomId AND FeatureId = @FeatureId;

    SELECT f.Id, f.Category, f.Description
    FROM ListingRoomFeature lrf
    INNER JOIN Feature f ON f.Id = lrf.FeatureId
    WHERE lrf.ListingRoomId = @ListingRoomId;
END

GO

