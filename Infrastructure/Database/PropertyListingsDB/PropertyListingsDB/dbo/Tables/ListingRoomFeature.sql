CREATE TABLE [dbo].[ListingRoomFeature] (
    [Id]            INT IDENTITY (1, 1) NOT NULL,
    [ListingRoomId] INT NULL,
    [FeatureId]     INT NULL,
    CONSTRAINT [PK__ListingR__D9A930DD7C93E76B] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingRoomFeature_Feature] FOREIGN KEY ([FeatureId]) REFERENCES [dbo].[Feature] ([Id]),
    CONSTRAINT [FK_ListingRoomFeature_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id]),
    CONSTRAINT [UQ_ListingRoomFeature_RoomId_FeatureId] UNIQUE NONCLUSTERED ([ListingRoomId] ASC, [FeatureId] ASC)
);


GO

