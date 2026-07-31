CREATE TABLE [dbo].[ListingAddress] (
    [ListingAddressId] INT            IDENTITY (1, 1) NOT NULL,
    [ListingId]        INT            NOT NULL,
    [ErfNumber]        NVARCHAR (50)  NULL,
    [EstateName]       NVARCHAR (255) NULL,
    [StreetNumber]     NVARCHAR (20)  NULL,
    [UnitNumber]       NVARCHAR (20)  NULL,
    [Street]           NVARCHAR (255) NULL,
    [Suburb]           NVARCHAR (100) NULL,
    [City]             NVARCHAR (100) NULL,
    [Province]         NVARCHAR (100) NULL,
    [Country]          NVARCHAR (100) NULL,
    [PostalCode]       NVARCHAR (20)  NULL,
    [Latitude]         DECIMAL (9, 6) NULL,
    [Longitude]        DECIMAL (9, 6) NULL,
    CONSTRAINT [PK__ListingA__ED73BF7A8659A885] PRIMARY KEY CLUSTERED ([ListingAddressId] ASC),
    CONSTRAINT [FK__ListingAd__Listi__7226EDCC] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_ListingAddress_ListingId] UNIQUE NONCLUSTERED ([ListingId] ASC)
);


GO

