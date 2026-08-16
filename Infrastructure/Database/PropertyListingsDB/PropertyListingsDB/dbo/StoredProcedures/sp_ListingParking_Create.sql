
CREATE   PROCEDURE sp_ListingParking_Create
    @ListingId INT,
    @ParkingTypeId INT,
    @Quantity INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO ListingParking (ListingId, ParkingTypeId, Quantity)
    VALUES (@ListingId, @ParkingTypeId, @Quantity);

    DECLARE @Id INT = SCOPE_IDENTITY();

    SELECT lp.Id, lp.ListingId, lp.ParkingTypeId, lp.Quantity, pt.Description AS ParkingTypeDescription
    FROM ListingParking lp
    INNER JOIN ParkingType pt ON pt.Id = lp.ParkingTypeId
    WHERE lp.Id = @Id;
END

GO

