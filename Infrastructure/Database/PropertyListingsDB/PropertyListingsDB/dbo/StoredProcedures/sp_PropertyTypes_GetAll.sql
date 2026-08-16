-- ============================================
-- Reference / Lookup Data Stored Procedures
-- ============================================

-- PropertyTypes
CREATE   PROCEDURE sp_PropertyTypes_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, SortOrder, IsActive
    FROM PropertyType
    WHERE IsActive = 1
    ORDER BY SortOrder ASC;
END

GO

