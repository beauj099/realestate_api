
-- Submit listing (incomplete -> submitted)
CREATE   PROCEDURE sp_Listings_Submit
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Listings
    SET Status = 'submitted',
        ListDate = GETUTCDATE(),
        UpdatedAt = GETUTCDATE()
    WHERE Id = @Id AND Status = 'incomplete';

    SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt
    FROM Listings
    WHERE Id = @Id;
END

GO

