CREATE TABLE [dbo].[ListingRoom] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [ListingId]     INT            NOT NULL,
    [Name]          NVARCHAR (200) NOT NULL,
    [RoomTypeId]    INT            NOT NULL,
    [RoomTypeOther] NVARCHAR (200) NULL,
    [PhotoUrl]      NVARCHAR (500) NULL,
    [CreatedAt]     DATETIME2 (7)  NOT NULL,
    [UpdatedAt]     DATETIME2 (7)  NOT NULL,
    CONSTRAINT [PK__ListingR__B5B0CB527A653047] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK__ListingRo__Listi__7D98A078] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ListingRoom_RoomTypes] FOREIGN KEY ([RoomTypeId]) REFERENCES [dbo].[RoomTypes] ([Id]),
    INDEX [IX_ListingRoom_ListingId] NONCLUSTERED ([ListingId] ASC)
);


GO
ALTER TABLE [dbo].[ListingRoom] WITH CHECK CHECK CONSTRAINT [FK__ListingRo__Listi__7D98A078];


GO
ALTER TABLE [dbo].[ListingRoom] WITH CHECK CHECK CONSTRAINT [FK_ListingRoom_RoomTypes];


GO

