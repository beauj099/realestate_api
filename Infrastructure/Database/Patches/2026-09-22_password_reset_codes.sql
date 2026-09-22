-- Password reset codes for POST /api/auth/forgot-password and /api/auth/reset-password.
-- Not applied automatically -- run it once in SSMS or sqlcmd against the deployed
-- database. Safe to re-run: every step checks first.
--
-- Only a SHA-256 hash (base64) of each 6-digit code is stored, never the code itself.
-- A code is usable while UsedAt IS NULL, ExpiresAt > GETUTCDATE() and Attempts < 5.
-- Superseded / locked-out codes are invalidated by pulling ExpiresAt back to "now";
-- UsedAt is only set by a successful reset.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.PasswordResetCodes', 'U') IS NULL
    CREATE TABLE [dbo].[PasswordResetCodes] (
        [Id]        INT            IDENTITY (1, 1) NOT NULL,
        [UserId]    INT            NOT NULL,
        [CodeHash]  NVARCHAR (128) NOT NULL,
        [ExpiresAt] DATETIME       NOT NULL,
        [Attempts]  INT            CONSTRAINT [DF_PasswordResetCodes_Attempts] DEFAULT ((0)) NOT NULL,
        [UsedAt]    DATETIME       NULL,
        [CreatedAt] DATETIME       CONSTRAINT [DF_PasswordResetCodes_CreatedAt] DEFAULT (GETUTCDATE()) NOT NULL,
        CONSTRAINT [PK_PasswordResetCodes] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PasswordResetCodes_Users' AND parent_object_id = OBJECT_ID('dbo.PasswordResetCodes'))
    ALTER TABLE [dbo].[PasswordResetCodes] WITH CHECK
        ADD CONSTRAINT [FK_PasswordResetCodes_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PasswordResetCodes_UserId' AND object_id = OBJECT_ID('dbo.PasswordResetCodes'))
    CREATE NONCLUSTERED INDEX [IX_PasswordResetCodes_UserId] ON [dbo].[PasswordResetCodes] ([UserId] ASC);

COMMIT TRANSACTION;
