
CREATE   PROCEDURE sp_ListingRoomFeatures_Link
    @ListingRoomId INT,
    @FeatureId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM ListingRoomFeature WHERE ListingRoomId = @ListingRoomId AND FeatureId = @FeatureId)
    BEGIN
        INSERT INTO ListingRoomFeature (ListingRoomId, FeatureId)
        VALUES (@ListingRoomId, @FeatureId);
    END

    SELECT f.Id, f.Category, f.Description
    FROM ListingRoomFeature lrf
    INNER JOIN Feature f ON f.Id = lrf.FeatureId
    WHERE lrf.ListingRoomId = @ListingRoomId;
END

GO

