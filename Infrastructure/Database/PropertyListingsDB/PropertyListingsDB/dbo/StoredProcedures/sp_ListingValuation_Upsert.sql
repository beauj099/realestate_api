-- ============================================
-- ListingValuation & PropertyRunningCosts Stored Procedures
-- ============================================

CREATE   PROCEDURE sp_ListingValuation_Upsert
    @ListingId INT,
    @OwnersNetPrice DECIMAL(18,2) = NULL,
    @AgentValuation DECIMAL(18,2) = NULL,
    @CommissionPercent DECIMAL(5,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ValuationId INT;

    SELECT @ValuationId = ListingValuationId FROM Listing WHERE Id = @ListingId;

    IF @ValuationId IS NOT NULL
    BEGIN
        UPDATE ListingValuation
        SET OwnersNetPrice = @OwnersNetPrice,
            AgentValuation = @AgentValuation,
            CommissionPercent = @CommissionPercent
        WHERE Id = @ValuationId;
    END
    ELSE
    BEGIN
        INSERT INTO ListingValuation (OwnersNetPrice, AgentValuation, CommissionPercent)
        VALUES (@OwnersNetPrice, @AgentValuation, @CommissionPercent);

        SET @ValuationId = SCOPE_IDENTITY();

        UPDATE Listing
        SET ListingValuationId = @ValuationId, UpdatedAt = GETUTCDATE()
        WHERE Id = @ListingId;
    END

    SELECT Id, OwnersNetPrice, AgentValuation, CommissionPercent
    FROM ListingValuation
    WHERE Id = @ValuationId;
END

GO

