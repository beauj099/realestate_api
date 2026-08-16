
CREATE   PROCEDURE sp_Contacts_Create
    @ListingId INT,
    @FullName NVARCHAR(200) = NULL,
    @IdNumber NVARCHAR(50) = NULL,
    @CompanyName NVARCHAR(200) = NULL,
    @CompanyRegistrationNumber NVARCHAR(50) = NULL,
    @MobilePhone NVARCHAR(50) = NULL,
    @EmailAddress NVARCHAR(200) = NULL,
    @Role NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO Contact (ListingId, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role)
    VALUES (@ListingId, @FullName, @IdNumber, @CompanyName, @CompanyRegistrationNumber, @MobilePhone, @EmailAddress, @Role);

    DECLARE @Id INT = SCOPE_IDENTITY();

    SELECT Id, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, ListingId
    FROM Contact
    WHERE Id = @Id;
END

GO

