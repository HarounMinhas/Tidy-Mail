# Tidy Mail (MailboxCleaner)

Tidy Mail is a .NET 8 Blazor Server Gmail cleanup assistant for people with chaotic, overloaded, or hoarded inboxes. The product flow is now scan-first: sign in with Google, run a mailbox metadata scan, review dashboard statistics and cleanup suggestions from local cache, preview a write action, confirm it, then apply the Gmail action by message ID and update the local metadata cache.

## Product flow

1. Sign in with Google.
2. If no recent local scan exists, Tidy Mail shows a mailbox scan screen.
3. During scanning, the UI explains that only metadata is fetched and shows status, scanned count, total discovered messages when known, current Gmail page, and a progress bar.
4. Metadata is cached locally for the signed-in session/user.
5. Dashboard statistics, sender grouping, filtering, sorting, selections, and cleanup suggestions run from local metadata only.
6. Gmail is called again only when the user confirms a write action.
7. After Gmail confirms a write action, local metadata is updated for the successful message IDs.

## Metadata-only privacy approach

Tidy Mail never fetches or stores Gmail message bodies. Gmail reads use message metadata format and request only cleanup-safe metadata:

- Message ID
- Thread ID
- Sender name and email
- Sender domain
- Subject
- Received date
- Read/unread state
- Gmail labels
- Attachment presence
- Size estimate
- Scan timestamp
- Optional classification headers: `List-Unsubscribe` and `Precedence`

The app does not display or persist full email contents. Message IDs are used only to apply Gmail write actions after explicit confirmation.

## OAuth and Gmail scopes

Configure Google OAuth credentials with a redirect URI such as:

```text
https://localhost:5001/auth/callback
```

The default scope set is:

```text
openid email profile https://www.googleapis.com/auth/gmail.modify
```

Scopes are used as follows:

- `openid email profile`: signs the user in and identifies the current session.
- `gmail.modify`: required for archive, mark read, mark unread, label changes, and moving messages to trash. Reads still use metadata-only Gmail requests.

Access tokens are refreshed through a `UserCredential` built from the stored token set. If Google reports an invalid or revoked refresh token, stored tokens are cleared so the user can safely sign in again.

## How scanning works

Gmail list responses provide message IDs and thread IDs rather than all metadata needed for cleanup. Tidy Mail therefore pages through Gmail IDs and fetches message metadata for each message using Gmail metadata format. The scanner is designed around local cache and progress reporting so large mailboxes remain usable even though per-message metadata calls are still required.

Current scan behavior:

- Gmail list page size is configured for up to 500 messages.
- Metadata reads request `From`, `Subject`, `Date`, `List-Unsubscribe`, and `Precedence` headers.
- Metadata fetching is concurrency-limited.
- Scan state tracks scan ID, user/session, start/completion time, page token, discovered/scanned counts, status, errors, and cached metadata.
- A completed scan is reused instead of rescanning on every page load.
- Stale scans can be refreshed manually.

## Dashboard and cleanup suggestions

The dashboard uses local metadata only to show:

- Total scanned messages
- Read and unread counts
- Messages with attachments
- Messages older than 6 months and 1 year
- Top senders and domains
- Top noreply senders
- Likely newsletters
- Largest cleanup groups
- Recent scan date

Cleanup suggestions are explainable and metadata-only. Suggestions include old read mail, old unread mail, old newsletters, high-volume noreply senders, notification senders, bulk senders, and high-volume domains. Tidy Mail never auto-deletes anything; every action requires preview and confirmation.

## Filtering, sorting, and selection

Filtering and selection operate against the local metadata cache and do not call Gmail. Supported metadata filters include sender, domain, subject keyword, read/unread, attachment presence, age buckets, label, noreply, newsletter-like, and notification-like messages.

Sorting supports sender name, email, message date, unread state, attachment state, and sender/domain grouping helpers.

## Bulk Gmail actions

Before a Gmail write action, the UI builds a preview with the action, affected count, top affected senders, sample messages, and a risk warning for trash. Empty selections cannot be confirmed.

Supported actions:

- Trash selected messages
- Archive selected messages by removing `INBOX`
- Mark selected messages read by removing `UNREAD`
- Mark selected messages unread by adding `UNREAD`
- Move selected messages to an existing label
- Create a label and move selected messages to it

Batch modify is used for Gmail label modifications where possible with chunks of up to 1000 message IDs. Trash keeps Gmail trash semantics. Result objects report total requested, succeeded, failed, failed IDs, error messages, and partial success state. Local cache updates are applied only for successful message IDs.

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

## Benchmarks

A BenchmarkDotNet project is available at `benchmarks/MailboxCleaner.Benchmarks`.

Run benchmarks with:

```bash
dotnet run -c Release --project benchmarks/MailboxCleaner.Benchmarks/MailboxCleaner.Benchmarks.csproj
```

Benchmarks cover filtering, sender grouping, domain grouping, autocomplete, bulk selection, cleanup suggestion generation, and dashboard statistics/metadata mapping over generated datasets of 100, 1,000, 10,000, and 50,000 messages.

## Known limitations and future improvements

- Gmail list responses only return IDs/thread IDs, so per-message metadata fetches are still required.
- Very large mailboxes benefit from caching and progressive scan, but initial scans still take time.
- Future incremental sync can use Gmail history IDs to avoid full rescans.
- Session/memory cache is suitable for MVP; SQLite or a database should replace it for durable multi-device use.
- Trash APIs have different semantics than label modification; partial failure reporting remains important for large cleanup operations.
