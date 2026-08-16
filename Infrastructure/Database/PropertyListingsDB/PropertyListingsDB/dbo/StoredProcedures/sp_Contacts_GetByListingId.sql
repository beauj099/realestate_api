-- ============================================
-- Contact Stored Procedures
-- ============================================

CREATE   PROCEDURE sp_Contacts_GetByListingId
    @ListingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, ListingId
    FROM Contact
    WHERE ListingId = @ListingId
    ORDER BY FullName;
END

GO

