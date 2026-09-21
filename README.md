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