
-- List all listings with optional filters
CREATE   PROCEDURE sp_Listings_GetAll
    @Status NVARCHAR(20) = NULL,
    @DateFrom DATETIME = NULL,
    @DateTo DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt
    FROM Listing
    WHERE (@Status IS NULL OR Status = @Status)
      AND (@DateFrom IS NULL OR CreatedAt >= @DateFrom)
      AND (@DateTo IS NULL OR CreatedAt <= @DateTo)
    ORDER BY CreatedAt DESC;
END

GO

