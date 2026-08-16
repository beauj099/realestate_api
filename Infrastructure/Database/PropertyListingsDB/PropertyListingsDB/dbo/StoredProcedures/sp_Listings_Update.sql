
-- Update listing-level fields
CREATE   PROCEDURE sp_Listings_Update
    @Id INT,
    @Status NVARCHAR(20) = NULL,
    @P24Ref NVARCHAR(50) = NULL,
    @PropertyTypeId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Listing
    SET Status = COALESCE(@Status, Status),
        P24Ref = COALESCE(@P24Ref, P24Ref),
        PropertyTypeId = COALESCE(@PropertyTypeId, PropertyTypeId),
        UpdatedAt = GETUTCDATE()
    WHERE Id = @Id;

    SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt
    FROM Listing
    WHERE Id = @Id;
END

GO

