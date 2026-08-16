# Cloudflare R2 Setup

## 1. Create an R2 bucket

- Go to Cloudflare Dashboard → R2 → Create bucket
- Name it (e.g., `realestate-images`)
- Choose a location (auto is fine)

## 2. Generate API token

- In R2 → Manage R2 API Tokens → Create API token
- Permissions: **Admin Read & Write**
- Copy the **Access Key ID** and **Secret Access Key**

## 3. Enable public access

- Open your bucket → Settings → Public access
- Either connect a **custom domain** or use the **`r2.dev`** subdomain
- Copy the **public URL** (e.g., `https://pub-xxxxxxxxxxxxxxxxxxxxxxxxxxxxx.r2.dev`)

## 4. Provide the R2 values as local secrets

> ⚠️ **Do not put real secrets in `appsettings.json`** — it is committed to git. Use
> `appsettings.Development.json` (now git-ignored) or user-secrets instead.

```json
"R2": {
  "BucketName": "realestate-images",
  "AccessKeyId": "<from step 2>",
  "SecretAccessKey": "<from step 2>",
  "Endpoint": "https://<your-account-id>.r2.cloudflarestorage.com",
  "PublicUrl": "<from step 3>"
}
```

> **Tip for `Endpoint`:** Your account ID is in the R2 dashboard URL: `https://dash.cloudflare.com/<account-id>/r2`

> **Tip for `PublicUrl`:** Use the full URL with the bucket name as path if using `r2.dev`:
> `https://pub-<hash>.r2.dev` (not the `cf-r2.com` endpoint)

# JWT Secret Setup (required)

The API **will not start** until a secure JWT signing secret is configured (at least 32
bytes, and not the old placeholder). Keep it out of source control — set it via user-secrets
in development:

```bash
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
```

Or via an environment variable (any host / container):

```bash
export Jwt__Secret="<64+ random characters>"
```

The committed `appsettings.json` intentionally ships an **empty** `Jwt:Secret`.
