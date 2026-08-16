-- ============================================
-- ListingParking Stored Procedures
-- ============================================

CREATE   PROCEDURE sp_ListingParking_GetByListingId
    @ListingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT lp.Id, lp.ListingId, lp.ParkingTypeId, lp.Quantity, pt.Description AS ParkingTypeDescription
    FROM ListingParking lp
    INNER JOIN ParkingType pt ON pt.Id = lp.ParkingTypeId
    WHERE lp.ListingId = @ListingId
    ORDER BY pt.Description;
END

GO

