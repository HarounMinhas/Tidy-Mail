# Tidy Mail (MailboxCleaner)

Tidy Mail is a .NET 8 Blazor Server MVP that connects to Gmail, reads **metadata only**, and shows a sender overview with search, filtering, and sorting.

## Features (MVP)

- Google OAuth2 login
- Gmail API metadata access (no message bodies)
- Sender overview grouped by email
- Search, domain filter, noreply filter, and sorting

## Prerequisites

- .NET 8 SDK
- Google account
- Google Cloud project

## Google Cloud setup

1. Create a Google Cloud project.
2. Enable the **Gmail API**.
3. Configure OAuth consent screen:
   - Type: External
   - Add your Gmail address as a test user
4. Create OAuth credentials:
   - Type: Web application
   - Authorized redirect URI: `https://localhost:5001/auth/callback`
   - Authorized JavaScript origins: `https://localhost:5001`

## Configure secrets locally

```bash
cd src/MailboxCleaner.Web
dotnet user-secrets init
dotnet user-secrets set "Google:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Google:ClientSecret" "YOUR_CLIENT_SECRET"
dotnet user-secrets set "Google:RedirectUri" "https://localhost:5001/auth/callback"
```

## Run locally

```bash
dotnet restore
dotnet run
```

Open `https://localhost:5001`, click **Login with Google**, and visit **Overview**.

## Troubleshooting

- **Redirect URI mismatch**: verify the exact URI and port.
- **Consent screen**: ensure your account is listed as a test user.
- **Gmail API disabled**: make sure the API is enabled in your project.

## Deployment notes

- Store secrets in environment variables or a secrets manager.
- Update redirect URIs in Google Cloud to match production.
- Use HTTPS in production.

