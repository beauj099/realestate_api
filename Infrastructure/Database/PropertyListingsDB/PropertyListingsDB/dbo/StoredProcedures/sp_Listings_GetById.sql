
-- Get listing by ID
CREATE   PROCEDURE sp_Listings_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt
    FROM Listing
    WHERE Id = @Id;
END

GO

