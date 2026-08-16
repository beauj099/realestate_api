
CREATE   PROCEDURE sp_Contacts_Update
    @Id INT,
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
    UPDATE Contact
    SET FullName = COALESCE(@FullName, FullName),
        IdNumber = COALESCE(@IdNumber, IdNumber),
        CompanyName = COALESCE(@CompanyName, CompanyName),
        CompanyRegistrationNumber = COALESCE(@CompanyRegistrationNumber, CompanyRegistrationNumber),
        MobilePhone = COALESCE(@MobilePhone, MobilePhone),
        EmailAddress = COALESCE(@EmailAddress, EmailAddress),
        Role = COALESCE(@Role, Role)
    WHERE Id = @Id;

    SELECT Id, FullName, IdNumber, CompanyName, CompanyRegistrationNumber, MobilePhone, EmailAddress, Role, ListingId
    FROM Contact
    WHERE Id = @Id;
END

GO

