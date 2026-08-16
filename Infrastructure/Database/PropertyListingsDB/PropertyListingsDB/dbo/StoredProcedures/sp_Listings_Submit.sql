
-- Submit listing (draft -> submitted)
CREATE   PROCEDURE sp_Listings_Submit
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Listing
    SET Status = 'submitted',
        ListDate = GETUTCDATE(),
        UpdatedAt = GETUTCDATE()
    WHERE Id = @Id AND Status = 'draft';

    SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt
    FROM Listing
    WHERE Id = @Id;
END

GO

