
-- Room Condition
CREATE   PROCEDURE sp_Condition_Upsert
    @ListingRoomId INT,
    @ConditionRating DECIMAL(3,1) = NULL,
    @Notes NVARCHAR(MAX) = NULL,
    @ConditionCategoryId INT
AS
BEGIN
    SET NOCOUNT ON;

    MERGE Condition AS target
    USING (SELECT @ListingRoomId AS ListingRoomId) AS source
    ON target.ListingRoomId = source.ListingRoomId
    WHEN MATCHED THEN
        UPDATE SET
            ConditionRating = @ConditionRating,
            Notes = @Notes,
            ConditionCategoryId = @ConditionCategoryId
    WHEN NOT MATCHED THEN
        INSERT (ListingRoomId, ConditionRating, Notes, ConditionCategoryId)
        VALUES (@ListingRoomId, @ConditionRating, @Notes, @ConditionCategoryId);

    SELECT Id, ListingRoomId, ConditionRating, Notes, ConditionCategoryId
    FROM Condition
    WHERE ListingRoomId = @ListingRoomId;
END

GO

