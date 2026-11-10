# Tidy Mail (MailboxCleaner)

Tidy Mail is a .NET 8 Blazor Server Gmail cleanup assistant for people with chaotic, overloaded, or hoarded inboxes. The product flow is now scan-first: sign in with Google, run a mailbox metadata scan, review dashboard statistics and cleanup suggestions from local cache, preview a write action, confirm it, then apply the Gmail action by message ID and update the local metadata cache.

## Product flow

1. Sign in with Google.
2. If no recent local scan exists, Tidy Mail shows a mailbox scan screen.
3. During scanning, the UI explains that only metadata is fetched and shows status, scanned count, total discovered messages when known, current Gmail page, and a progress bar.
4. Metadata is cached by validated Google account subject in a local SQLite database; untrusted/anonymous sessions are never used as mailbox cache keys.
5. Dashboard statistics, sender grouping, filtering, sorting, selections, and cleanup suggestions run from local metadata only.
6. Gmail is called again only when the user confirms a write action.
7. After Gmail acknowledges a write action, local metadata is updated only for acknowledged message IDs. Rejected and uncertain results are shown separately; the UI avoids automatic retries for uncertain messages.

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
https://localhost:51179/auth/callback
```

The default scope set is:

```text
openid email profile https://www.googleapis.com/auth/gmail.modify
```

Scopes are used as follows:

- `openid email profile`: signs the user in and identifies the current session.
- `gmail.modify`: required for archive, mark read, mark unread, label changes, and moving messages to trash. Reads still use metadata-only Gmail requests.

Access tokens are refreshed through a `UserCredential` built from the stored token set. The Google ID token is used to sign in with real account claims when available. If Google reports an invalid or revoked refresh token, stored tokens are cleared so the user can safely sign in again.

## How scanning works

Gmail list responses provide message IDs and thread IDs rather than all metadata needed for cleanup. Tidy Mail therefore pages through Gmail IDs and fetches message metadata for each message using Gmail metadata format. The scanner is designed around local cache and progress reporting so large mailboxes remain usable even though per-message metadata calls are still required.

Current scan behavior:

- Gmail list page size is configured for up to 500 messages.
- Metadata reads request `From`, `Subject`, `Date`, `List-Unsubscribe`, and `Precedence` headers.
- Metadata fetching is concurrency-limited.
- Scan state tracks scan ID, user/session, start/completion time, page token, discovered/scanned counts, status, errors, and cached metadata.
- A completed scan is reused instead of rescanning on every page load; SQLite metadata survives an application restart.
- Full rescans replace the prior metadata set so messages removed outside Tidy Mail do not linger in the dashboard. When a valid Gmail history cursor is available, refresh uses incremental History API updates and falls back to a full scan if the cursor has expired.
- Stale scans can be refreshed manually; an explicit full scan bypasses the short-lived Gmail in-process cache.

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

Cleanup suggestions are explainable and metadata-only. Suggestions include old read mail, old unread mail, old newsletters, high-volume noreply senders, notification senders, bulk senders, and high-volume domains. Clicking a suggestion **only preselects messages for review**; it never automatically chooses Archive, Trash or any other operation. Every Gmail mutation requires a separate action choice, a preview and explicit confirmation.

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
- Move selected messages to an existing label (**removes INBOX and other custom labels**, keeping system flags)
- Create a label and move selected messages to it
- Add an existing/new label **without removing INBOX or any existing labels**

Batch modify is used for Gmail label modifications where possible with chunks of up to 1000 message IDs. Trash keeps Gmail trash semantics. Result objects distinguish **Gmail-acknowledged**, **definitively rejected**, and **uncertain** message IDs (for example, a network timeout after sending). The UI offers a fresh confirmation for rejected IDs after reconciliation; uncertain IDs are not blindly retried because Gmail may already have applied them. SQLite cache updates only acknowledged IDs, and failed local cache writes do not erase Gmail acknowledgements. A move removes prior user labels; adding a label never removes them.

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
dotnet user-secrets set "Google:RedirectUri" "https://localhost:51179/auth/callback"
```

## Run locally

```bash
dotnet restore
dotnet run --project src/MailboxCleaner.Web/MailboxCleaner.Web.csproj
```

Open `https://localhost:51179`, sign in with Google, and visit **Overview**.

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

GitHub Actions runs a .NET 8 restore, build, and test workflow for pull requests. A passing CI run is not a substitute for a real Gmail OAuth/browser test.

## Local SQLite storage and account security

By default, metadata is stored in a SQLite file under the OS user-local application data directory (`TidyMail/mailbox.db`). To choose a path, set `Mailbox:SqlitePath` in configuration or the environment (for example `Mailbox__SqlitePath`). This is a **single-instance storage design**: use a managed shared database for several web servers and secure the host volume and backups. The file contains sender, subject and other cleanup metadata, but never email bodies.

OAuth ID tokens are verified against Google and the expected client audience, and each mailbox cache is keyed by a validated Google subject. Access and refresh tokens are **Data Protection-encrypted and persisted in SQLite** by validated Google subject. The Data Protection key ring is stored in the OS local application data directory under `TidyMail/keys` by default (override with `Mailbox:DataProtectionKeysPath`). Keep both the SQLite file **and** the key ring private, persistent and backed up; losing the key ring renders saved tokens unreadable. A multi-instance deployment requires a shared database and shared protected key ring, which is not yet provided by this local setup.

## Important remaining validation

The core flows compile and have automated tests, but the application still needs real browser and Gmail OAuth end-to-end tests with **separate test accounts**. Verify that Gmail really applies every action, inspect expired/revoked credentials, uncertain API outcomes, multiuser isolation, large-mailbox performance and account disconnect behavior before public deployment. No production Gmail messages were modified as part of this development branch.

## Known limitations and future improvements

- Gmail list responses only return IDs/thread IDs, so per-message metadata fetches are still required on a full scan. Attachment detection uses metadata-safe `has:attachment` message ID searches.
- Very large mailboxes benefit from caching and progressive scan, but initial scans still take time.
- Incremental sync through Gmail history IDs has been implemented but still requires live-account validation, including expired-cursor fallbacks.
- SQLite metadata caching now survives local restarts. Shared database and durable encrypted token storage are still necessary for multi-instance production use.
- Trash APIs have different semantics than label modification; partial failure reporting remains important for large cleanup operations.
