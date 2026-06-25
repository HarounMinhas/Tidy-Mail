# Tidy Mail (MailboxCleaner)

Tidy Mail is a .NET 8 Blazor Server application for reviewing Gmail sender activity and applying Gmail-native cleanup actions while keeping Gmail reads metadata-only.

## Implemented features

- Google OAuth2 login with Gmail modify scope.
- Gmail API metadata loading with no message body retrieval. The app requests Gmail message `metadata` format and only asks for `From`, `Subject`, and `Date` headers.
- Session-scoped metadata and label caching to reduce repeated Gmail API calls; write actions invalidate cache and trigger a refresh.
- Sender overview grouped by sender email with expandable message rows.
- Filtering by sender/name, domain, keyword, read/unread state, folder/label, attachments, and noreply senders.
- Cached autocomplete suggestions for sender, sender name, and subject values.
- Sorting by sender name, email, message count, newest message, oldest message, ascending or descending.
- Bulk Gmail actions:
  - Move messages to Gmail Trash.
  - Archive messages by removing `INBOX`.
  - Mark read by removing `UNREAD`.
  - Mark unread by adding `UNREAD`.
  - Move to an existing Gmail label.
  - Create a new Gmail label and move selected messages to it.
- Professional UX states for loading, progress, disabled bulk actions, confirmation, success, and failure messages.
- Retry and user-facing error handling around Gmail API operations.
- Unit/integration-style tests for filtering, grouping-oriented engine behavior, and Gmail action orchestration.

## Architecture

The application preserves a simple layered structure:

- **Components** (`src/MailboxCleaner.Web/Components`) render the Blazor UI and keep interaction state.
- **Application services** (`src/MailboxCleaner.Web/Application/Services`) aggregate Gmail metadata, filter/sort data, and orchestrate Gmail actions.
- **Infrastructure** (`src/MailboxCleaner.Web/Infrastructure/Google`) owns OAuth and Gmail API integration.
- **Domain/DTOs** hold sender identifiers, sender statistics, and message metadata projections.

`GmailClient` is the only class that talks directly to the Gmail API. UI and application services depend on `IGmailClient`, which makes Gmail action orchestration testable without network calls.

## OAuth and scopes

Configure Google OAuth credentials with a redirect URI such as:

```text
https://localhost:5001/auth/callback
```

The default scope set is:

```text
openid email profile https://www.googleapis.com/auth/gmail.modify
```

`gmail.modify` is required because the app performs Gmail-native trash, archive, read/unread, and label modifications. The app still reads message metadata only and does not fetch message bodies.

## Privacy and metadata-only approach

Tidy Mail intentionally avoids email body access. Metadata loading uses Gmail message metadata requests and limits headers to:

- `From`
- `Subject`
- `Date`

Message IDs are used only when Gmail write operations require them. The app derives read state, archive state, folders, labels, and attachment presence from Gmail labels and payload metadata.

## Performance notes

- Gmail message lists are paged through incrementally instead of requesting an unbounded page.
- Message metadata fetches are concurrency-limited.
- Cancellation tokens are propagated through Gmail list, get, label, and write calls.
- Metadata and labels are cached per scoped Gmail client session.
- Sender grouping and autocomplete use precomputed collections to avoid repeated LINQ work during UI interactions.
- Filtering and sorting operate over local metadata only, so typing remains responsive for large metadata sets.

## Prerequisites

- .NET 8 SDK
- Google account
- Google Cloud project with Gmail API enabled

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
dotnet run --project src/MailboxCleaner.Web/MailboxCleaner.Web.csproj
```

Open `https://localhost:5001`, sign in with Google, and visit **Overview**.

## Testing

```bash
dotnet restore
dotnet build -c Release
dotnet test
```

## Remaining limitations and future work

- Gmail token refresh is not yet fully automated when an access token expires; users may need to sign in again.
- Very large mailboxes still require one metadata request per message ID because Gmail list responses do not include all required metadata.
- Benchmarks are documented in the performance notes and covered by scalable code paths, but no dedicated BenchmarkDotNet project is included yet.
- Bulk Gmail actions currently execute per message with retry; Gmail batch APIs could further reduce round trips in a future iteration.
