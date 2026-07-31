CREATE TABLE [dbo].[ListingBuildingInfo] (
    [Id]               INT             IDENTITY (1, 1) NOT NULL,
    [ListingId]        INT             NOT NULL,
    [ErfSize]          DECIMAL (12, 2) NULL,
    [FloorArea]        DECIMAL (12, 2) NULL,
    [ConstructionYear] INT             NULL,
    [FacingId]         INT             NULL,
    [ZoningId]         INT             NULL,
    CONSTRAINT [PK__ListingB__B6FF7EDE58812F51] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK__ListingBu__Listi__75F77EB0] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ListingBuildingInfo_Facing] FOREIGN KEY ([FacingId]) REFERENCES [dbo].[Facing] ([Id]),
    CONSTRAINT [FK_ListingBuildingInfo_Zoning] FOREIGN KEY ([ZoningId]) REFERENCES [dbo].[Zoning] ([Id]),
    CONSTRAINT [UQ_ListingBuildingInfo_ListingId] UNIQUE NONCLUSTERED ([ListingId] ASC)
);


GO

