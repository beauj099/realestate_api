CREATE TABLE [dbo].[ListingOutdoorFeature] (
    [Id]          INT IDENTITY (1, 1) NOT NULL,
    [ListingId]   INT NOT NULL,
    [Description] NVARCHAR (255) NOT NULL,
    CONSTRAINT [PK_ListingOutdoorFeature] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingOutdoorFeature_Listing]
        FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE
);
