# realestate_api

| Username | Password | Role  | Display Name |
|----------|----------|-------|--------------|
| `admin`  | `admin123` | Admin | Admin User   |
| `agent1` | `admin123` | Agent | Agent User   |

## Seeding users into the deployed database

Passwords are stored as BCrypt hashes (verified by the API on login), so the
plaintext password below must be replaced with a matching hash. The hashes in
this script correspond to the credentials in the table above.

Run this in SSMS or `sqlcmd` against the deployed database:

```sql
-- Adds the admin user (password: admin123)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Username] = 'admin')
BEGIN
    INSERT INTO [dbo].[Users] ([Username], [PasswordHash], [DisplayName], [Role], [IsActive])
    VALUES (
        'admin',
        '$2a$11$0Q7V1o6pT/qT5qacQrURtOfbNGeV63KWd7NIFZzsnAMYNHsVZmFXO',
        'Admin User',
        'Admin',
        1
    );
END;

-- Adds the agent user (password: admin123)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Username] = 'agent1')
BEGIN
    INSERT INTO [dbo].[Users] ([Username], [PasswordHash], [DisplayName], [Role], [IsActive])
    VALUES (
        'agent1',
        '$2a$11$D5Ae3xUkAJg6qXnX50ycYeJywZqokrdufAc2RaDWUfWCIaF1JEOn.',
        'Agent User',
        'Agent',
        1
    );
END;
```

To add a user with a different password, generate a BCrypt hash first (for
example `BCrypt.Net.BCrypt.HashPassword("yourPassword")`), then substitute it
into the `PasswordHash` column. Never insert a plaintext password.

## Password reset & email setup

Two endpoints drive password reset by email:

| Method | Route | Behaviour |
|--------|-------|-----------|
| `POST` | `/api/auth/forgot-password` | Body: `{"email": "..."}`. Always `204` for a well-formed email (whether or not the account exists), so emails cannot be enumerated. Emails a 6-digit code when an active account matches. |
| `POST` | `/api/auth/reset-password` | Body: `{"email": "...", "code": "123456", "newPassword": "..."}`. `204` on success, `400` for an invalid/expired/used code or a password under 6 characters. |

The code expires after **15 minutes**, allows **5 wrong attempts** before it is
locked, and is single-use. A successful reset revokes all of the user's
refresh tokens.

### SMTP configuration

Real mail is sent only when `Smtp:Host` is set. Add this to
`appsettings.Local.json` (git-ignored) or set environment variables
(`Smtp__Host`, `Smtp__Password`, …) on the server — see
`appsettings.Local.example.json` for the full key list:

```json
"Smtp": {
  "Host": "smtp.example.com",
  "Port": 587,
  "EnableSsl": true,
  "Username": "smtp-user@example.com",
  "Password": "<app password>",
  "FromAddress": "no-reply@realworth.co.za",
  "FromName": "RealWorth"
}
```

If `Smtp:Host` is empty, no email is sent: the message (including the reset
code) is written to the console log instead — fine for local development,
never for production.

### Rate limits

Both endpoints are rate limited **per client IP** and return `429` with a
`application/problem+json` body when exceeded:

- `forgot-password`: 5 requests per 10 minutes
- `reset-password`: 10 requests per 5 minutes