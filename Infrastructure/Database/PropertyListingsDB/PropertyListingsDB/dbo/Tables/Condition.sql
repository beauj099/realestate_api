CREATE TABLE [dbo].[Condition] (
    [Id]                  INT            IDENTITY (1, 1) NOT NULL,
    [ListingRoomId]       INT            NULL,
    [ConditionRating]     DECIMAL (3, 1) NULL,
    [Notes]               NVARCHAR (MAX) NULL,
    [ConditionCategoryId] INT            NULL,
    CONSTRAINT [PK_Condition] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UX_Condition_ListingRoomId] UNIQUE NONCLUSTERED ([ListingRoomId] ASC),
    CONSTRAINT [FK_Condition_ConditionCategory] FOREIGN KEY ([ConditionCategoryId]) REFERENCES [dbo].[ConditionCategory] ([Id]),
    CONSTRAINT [FK_Condition_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id])
);


GO

