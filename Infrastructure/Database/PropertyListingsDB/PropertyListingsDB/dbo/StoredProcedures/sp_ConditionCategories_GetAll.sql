
-- ConditionCategories
CREATE   PROCEDURE sp_ConditionCategories_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Description
    FROM ConditionCategory
    ORDER BY Description;
END

GO

