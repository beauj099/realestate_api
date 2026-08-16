
-- Delete a listing and all of its child records.
-- Only ListingAddress, ListingBuildingInfo, ListingOutdoorFeature and ListingRoom
-- cascade from Listings; Contact, ListingParking and PropertyRunningCosts do not,
-- and the room grandchildren (Condition, ListingRoomFeature, ListingRoomCustomFeature)
-- have no cascade from ListingRoom. Those are removed explicitly, in dependency
-- order, inside a transaction -- otherwise the delete fails on an FK violation.
CREATE   PROCEDURE sp_Listings_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Listings holds the FK to its valuation, so capture it before deleting.
        DECLARE @ValuationId INT;
        SELECT @ValuationId = ListingValuationId FROM Listings WHERE Id = @Id;

        -- Room grandchildren (no cascade from ListingRoom).
        DELETE FROM Condition
        WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id);

        DELETE FROM ListingRoomFeature
        WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id);

        DELETE FROM ListingRoomCustomFeature
        WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id);

        -- Rooms (these cascade from Listings, but are removed here so the
        -- ordering relative to their grandchildren is explicit).
        DELETE FROM ListingRoom WHERE ListingId = @Id;

        -- Direct children of the listing that do NOT cascade.
        DELETE FROM Contact WHERE ListingId = @Id;
        DELETE FROM ListingParking WHERE ListingId = @Id;
        DELETE FROM PropertyRunningCosts WHERE ListingId = @Id;

        -- The listing itself. Cascades remove ListingAddress,
        -- ListingBuildingInfo and ListingOutdoorFeature.
        DELETE FROM Listings WHERE Id = @Id;

        -- Now-orphaned valuation row (the listing held the FK to it).
        IF @ValuationId IS NOT NULL
            DELETE FROM ListingValuation WHERE Id = @ValuationId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END

GO

