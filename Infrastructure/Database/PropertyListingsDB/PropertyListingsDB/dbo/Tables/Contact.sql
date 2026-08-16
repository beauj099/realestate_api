CREATE TABLE [dbo].[Contact] (
    [Id]                        INT            IDENTITY (1, 1) NOT NULL,
    [FullName]                  NVARCHAR (255) NULL,
    [IdNumber]                  NVARCHAR (50)  NULL,
    [CompanyName]               NVARCHAR (255) NULL,
    [CompanyRegistrationNumber] NVARCHAR (100) NULL,
    [MobilePhone]               NVARCHAR (100) NULL,
    [EmailAddress]              NVARCHAR (255) NULL,
    [Role]                      NVARCHAR (50)  NULL,
    [ListingId]                 INT            NULL,
    CONSTRAINT [PK__Contact__5C66259B265703F0] PRIMARY KEY CLUSTERED ([Id] ASC),
    INDEX [IX_Contact_ListingId] NONCLUSTERED ([ListingId] ASC),
    CONSTRAINT [FK_Contact_Listing] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listing] ([Id])
);


GO

