
-- Delete a listing and all of its child records.
-- Not all child FKs use ON DELETE CASCADE (and room grandchildren have none),
-- so children are removed explicitly, in dependency order, inside a transaction.
CREATE   PROCEDURE sp_Listings_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- The listing owns the FK to its valuation, so capture it before deleting.
        DECLARE @ValuationId INT;
        SELECT @ValuationId = ListingValuationId FROM Listing WHERE Id = @Id;

        -- Room grandchildren (no cascade to ListingRoom).
        DELETE FROM Condition
        WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id);

        DELETE FROM ListingRoomFeature
        WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id);

        DELETE FROM ListingRoomCustomFeature
        WHERE ListingRoomId IN (SELECT Id FROM ListingRoom WHERE ListingId = @Id);

        -- Rooms.
        DELETE FROM ListingRoom WHERE ListingId = @Id;

        -- Other direct children of the listing.
        DELETE FROM Contact WHERE ListingId = @Id;
        DELETE FROM ListingParking WHERE ListingId = @Id;
        DELETE FROM ListingFeature WHERE ListingId = @Id;
        DELETE FROM PropertyRunningCosts WHERE ListingId = @Id;
        DELETE FROM ListingAddress WHERE ListingId = @Id;
        DELETE FROM ListingBuildingInfo WHERE ListingId = @Id;

        -- The listing itself must go before its valuation (it holds that FK).
        DELETE FROM Listing WHERE Id = @Id;

        -- Now-orphaned valuation row.
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
