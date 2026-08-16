
CREATE   PROCEDURE sp_ListingValuation_GetByListingId
    @ListingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT lv.Id, lv.OwnersNetPrice, lv.AgentValuation, lv.CommissionPercent
    FROM ListingValuation lv
    INNER JOIN Listing l ON l.ListingValuationId = lv.Id
    WHERE l.Id = @ListingId;
END

GO

