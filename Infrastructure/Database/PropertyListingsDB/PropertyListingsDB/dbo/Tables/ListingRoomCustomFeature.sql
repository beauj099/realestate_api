CREATE TABLE [dbo].[ListingRoomCustomFeature] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [ListingRoomId] INT            NULL,
    [Description]   NVARCHAR (100) NULL,
    CONSTRAINT [PK_ListingRoomCustomFeature] PRIMARY KEY CLUSTERED ([Id] ASC),
    INDEX [IX_ListingRoomCustomFeature_ListingRoomId] NONCLUSTERED ([ListingRoomId] ASC),
    CONSTRAINT [FK_ListingRoomCustomFeature_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id])
);


GO

