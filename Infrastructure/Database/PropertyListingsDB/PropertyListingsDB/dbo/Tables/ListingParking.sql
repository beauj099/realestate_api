CREATE TABLE [dbo].[ListingParking] (
    [Id]            INT IDENTITY (1, 1) NOT NULL,
    [ListingId]     INT NULL,
    [ParkingTypeId] INT NULL,
    [Quantity]      INT NULL,
    CONSTRAINT [PK_ListingParking] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingParking_Listing] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]),
    CONSTRAINT [FK_ListingParking_ParkingType] FOREIGN KEY ([ParkingTypeId]) REFERENCES [dbo].[ParkingType] ([Id])
);


GO

