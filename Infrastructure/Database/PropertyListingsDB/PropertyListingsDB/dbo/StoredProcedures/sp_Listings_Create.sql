-- ============================================
-- Listing CRUD Stored Procedures
-- ============================================

-- Create listing (incomplete status, auto-generate ReferenceNumber)
CREATE   PROCEDURE sp_Listings_Create
    @PropertyTypeId INT,
    @P24Ref NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Id INT;
    DECLARE @RefNum NVARCHAR(20);
    DECLARE @Year CHAR(4) = CAST(YEAR(GETDATE()) AS CHAR(4));
    DECLARE @NextNum INT;

    -- Generate reference number: LST-YYYY-NNNNN
    SELECT @NextNum = ISNULL(MAX(CAST(SUBSTRING(ReferenceNumber, 10, 5) AS INT)), 0) + 1
    FROM Listings
    WHERE ReferenceNumber LIKE 'LST-' + @Year + '-%';

    SET @RefNum = 'LST-' + @Year + '-' + RIGHT('00000' + CAST(@NextNum AS NVARCHAR(5)), 5);

    INSERT INTO Listings (ReferenceNumber, P24Ref, PropertyTypeId, Status, CreatedAt, UpdatedAt)
    VALUES (@RefNum, @P24Ref, @PropertyTypeId, 'incomplete', GETUTCDATE(), GETUTCDATE());

    SET @Id = SCOPE_IDENTITY();

    SELECT Id, ReferenceNumber, P24Ref, PropertyTypeId, ListingValuationId, ListDate, Status, CreatedAt, UpdatedAt
    FROM Listings
    WHERE Id = @Id;
END

GO

