CREATE TABLE [dbo].[PropertyType] (
    [Id]        INT           IDENTITY (1, 1) NOT NULL,
    [Name]      NVARCHAR (50) NULL,
    [SortOrder] INT           NULL,
    [IsActive]  BIT           NULL,
    CONSTRAINT [PK_PropertyType] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[RoomTypes] (
    [Id]          INT           IDENTITY (1, 1) NOT NULL,
    [Description] NVARCHAR (60) NULL,
    CONSTRAINT [PK_RoomTypes] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Feature] (
    [Id]          INT            NOT NULL,
    [Category]    NVARCHAR (100) NULL,
    [Description] NVARCHAR (100) NULL,
    CONSTRAINT [PK_Feature] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[ConditionCategory] (
    [Id]          INT           IDENTITY (1, 1) NOT NULL,
    [Description] NVARCHAR (60) NULL,
    CONSTRAINT [PK_ConditionCategory] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[ParkingType] (
    [Id]          INT           IDENTITY (1, 1) NOT NULL,
    [Description] NVARCHAR (60) NULL,
    CONSTRAINT [PK_ParkingType] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Facing] (
    [Id]          INT            NOT NULL,
    [Description] NVARCHAR (200) NULL,
    CONSTRAINT [PK_Facing] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Zoning] (
    [Id]          INT            NOT NULL,
    [Description] NVARCHAR (200) NULL,
    CONSTRAINT [PK_Zoning] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Users] (
    [Id]           INT            IDENTITY (1, 1) NOT NULL,
    [Username]     NVARCHAR (100) NOT NULL,
    [PasswordHash] NVARCHAR (500) NOT NULL,
    [DisplayName]  NVARCHAR (200) NOT NULL,
    [Role]         NVARCHAR (20)  DEFAULT ('Agent') NOT NULL,
    [IsActive]     BIT            DEFAULT ((1)) NOT NULL,
    [CreatedAt]    DATETIME2 (7)  DEFAULT (sysutcdatetime()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Users_Username] ON [dbo].[Users] ([Username] ASC);

CREATE TABLE [dbo].[ListingValuation] (
    [Id]                INT             IDENTITY (1, 1) NOT NULL,
    [OwnersNetPrice]    DECIMAL (14, 2) NULL,
    [AgentValuation]    DECIMAL (14, 2) NULL,
    [CommissionPercent] DECIMAL (5, 2)  NULL,
    CONSTRAINT [PK_ListingValuation] PRIMARY KEY CLUSTERED ([Id] ASC)
);

CREATE TABLE [dbo].[Listings] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [ReferenceNumber]    NVARCHAR (100) NULL,
    [P24Ref]             NVARCHAR (100) NULL,
    [PropertyTypeId]     INT            NULL,
    [ListingValuationId] INT            NULL,
    [ListDate]           DATE           NULL,
    [Status]             NVARCHAR (50)  DEFAULT ('Active') NOT NULL,
    [CreatedAt]          DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [UpdatedAt]          DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    CONSTRAINT [PK__Listings] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [CK__Listings__Status] CHECK ([Status] IN ('Active', 'Expired', 'Withdrawn', 'Let', 'Sold', 'incomplete', 'submitted')),
    CONSTRAINT [FK_Listings_ListingValuation] FOREIGN KEY ([ListingValuationId]) REFERENCES [dbo].[ListingValuation] ([Id]),
    CONSTRAINT [FK_Listings_PropertyType] FOREIGN KEY ([PropertyTypeId]) REFERENCES [dbo].[PropertyType] ([Id])
);

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
    CONSTRAINT [PK__ListingA] PRIMARY KEY CLUSTERED ([ListingAddressId] ASC),
    CONSTRAINT [FK__ListingAd__Listi] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_ListingAddress_ListingId] UNIQUE NONCLUSTERED ([ListingId] ASC)
);

CREATE TABLE [dbo].[ListingBuildingInfo] (
    [Id]               INT             IDENTITY (1, 1) NOT NULL,
    [ListingId]        INT             NOT NULL,
    [ErfSize]          DECIMAL (12, 2) NULL,
    [FloorArea]        DECIMAL (12, 2) NULL,
    [ConstructionYear] INT             NULL,
    [FacingId]         INT             NULL,
    [ZoningId]         INT             NULL,
    CONSTRAINT [PK__ListingB] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK__ListingBu__Listi] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ListingBuildingInfo_Facing] FOREIGN KEY ([FacingId]) REFERENCES [dbo].[Facing] ([Id]),
    CONSTRAINT [FK_ListingBuildingInfo_Zoning] FOREIGN KEY ([ZoningId]) REFERENCES [dbo].[Zoning] ([Id]),
    CONSTRAINT [UQ_ListingBuildingInfo_ListingId] UNIQUE NONCLUSTERED ([ListingId] ASC)
);

CREATE TABLE [dbo].[PropertyRunningCosts] (
    [Id]           INT             IDENTITY (1, 1) NOT NULL,
    [ListingId]    INT             NULL,
    [MonthlyLevy]  DECIMAL (12, 2) NULL,
    [MonthlyRates] DECIMAL (12, 2) NULL,
    [Electricity]  DECIMAL (12, 2) NULL,
    [Water]        DECIMAL (12, 2) NULL,
    CONSTRAINT [PK_PropertyExpenses] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PropertyRunningCosts_Listing] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id])
);

CREATE TABLE [dbo].[ListingRoom] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [ListingId]     INT            NOT NULL,
    [Name]          NVARCHAR (200) NOT NULL,
    [RoomTypeId]    INT            NOT NULL,
    [RoomTypeOther] NVARCHAR (200) NULL,
    [PhotoUrl]      NVARCHAR (500) NULL,
    [CreatedAt]     DATETIME2 (7)  NOT NULL,
    [UpdatedAt]     DATETIME2 (7)  NOT NULL,
    CONSTRAINT [PK_ListingRoom] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK__ListingRo__Listi] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ListingRoom_RoomTypes] FOREIGN KEY ([RoomTypeId]) REFERENCES [dbo].[RoomTypes] ([Id])
);

CREATE TABLE [dbo].[ListingParking] (
    [Id]            INT IDENTITY (1, 1) NOT NULL,
    [ListingId]     INT NULL,
    [ParkingTypeId] INT NULL,
    [Quantity]      INT NULL,
    CONSTRAINT [PK_ListingParking] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingParking_Listing] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]),
    CONSTRAINT [FK_ListingParking_ParkingType] FOREIGN KEY ([ParkingTypeId]) REFERENCES [dbo].[ParkingType] ([Id])
);

CREATE TABLE [dbo].[ListingOutdoorFeature] (
    [Id]          INT IDENTITY (1, 1) NOT NULL,
    [ListingId]   INT NOT NULL,
    [Description] NVARCHAR (255) NOT NULL,
    CONSTRAINT [PK_ListingOutdoorFeature] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingOutdoorFeature_Listing]
        FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]) ON DELETE CASCADE
);

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
    CONSTRAINT [PK__Contact] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Contact_Listing] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id])
);

CREATE TABLE [dbo].[Condition] (
    [Id]                  INT            IDENTITY (1, 1) NOT NULL,
    [ListingRoomId]       INT            NULL,
    [ConditionRating]     DECIMAL (3, 1) NULL,
    [Notes]               NVARCHAR (MAX) NULL,
    [ConditionCategoryId] INT            NULL,
    CONSTRAINT [PK_Condition] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Condition_ConditionCategory] FOREIGN KEY ([ConditionCategoryId]) REFERENCES [dbo].[ConditionCategory] ([Id]),
    CONSTRAINT [FK_Condition_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id])
);

CREATE TABLE [dbo].[ListingRoomFeature] (
    [Id]            INT IDENTITY (1, 1) NOT NULL,
    [ListingRoomId] INT NULL,
    [FeatureId]     INT NULL,
    CONSTRAINT [PK_ListingRoomFeature] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingRoomFeature_Feature] FOREIGN KEY ([FeatureId]) REFERENCES [dbo].[Feature] ([Id]),
    CONSTRAINT [FK_ListingRoomFeature_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id]),
    CONSTRAINT [UQ_ListingRoomFeature_Room_Feature] UNIQUE NONCLUSTERED ([ListingRoomId] ASC, [FeatureId] ASC)
);

CREATE TABLE [dbo].[ListingRoomCustomFeature] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [ListingRoomId] INT            NULL,
    [Description]   NVARCHAR (100) NULL,
    CONSTRAINT [PK_ListingRoomCustomFeature] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ListingRoomCustomFeature_ListingRoom] FOREIGN KEY ([ListingRoomId]) REFERENCES [dbo].[ListingRoom] ([Id])
);

CREATE TABLE [dbo].[RefreshTokens] (
    [Id]        INT            IDENTITY (1, 1) NOT NULL,
    [UserId]    INT            NOT NULL,
    [TokenHash] NVARCHAR (500) NOT NULL,
    [ExpiresAt] DATETIME2 (7)  NOT NULL,
    [CreatedAt] DATETIME2 (7)  DEFAULT (sysutcdatetime()) NOT NULL,
    [IsRevoked] BIT            DEFAULT ((0)) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id])
);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash] ON [dbo].[RefreshTokens] ([TokenHash] ASC);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_UserId] ON [dbo].[RefreshTokens] ([UserId] ASC);
