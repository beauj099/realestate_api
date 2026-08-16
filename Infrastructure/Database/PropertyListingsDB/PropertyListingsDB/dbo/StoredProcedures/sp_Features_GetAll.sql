
-- Features
CREATE   PROCEDURE sp_Features_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Category, Description
    FROM Feature
    ORDER BY Category, Description;
END

GO

