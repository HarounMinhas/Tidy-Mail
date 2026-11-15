# Dummy Gmail mailbox via Dependency Injection

**Status (16 November 2026):** implemented on [PR #13](https://github.com/HarounMinhas/Tidy-Mail/pull/13). This is a test harness, not a connection to Google. Production remains on the real Gmail implementation.

## Approach

The existing application uses `IGmailClient` for all Gmail operations. Instead of mocking every domain service separately, we replace only that external boundary in **explicit Development demo mode**:

```text
                           real mode (default)                 demo mode (Development only)
Blazor components ──> MailboxScanService / SenderOverviewService / GmailBulkActionService
                                     │
                                IGmailClient
                                     │
                         ┌───────────┴─────────────┐
                         │                         │
                    GmailClient              DemoGmailClient
                         │                         │
                   Google Gmail API         DemoMailboxStore
                   authenticated OAuth      48 synthetic emails/account
                         │                         │
                SqliteMailboxMetadataStore   MailboxMetadataStore
                SqliteTokenStore             DemoTokenStore (no real tokens)
```

Both paths use the same scan service, grouping, filtering, dashboard, cleanup suggestions, Gmail bulk action service, local action updates and history-merge code. This is significantly closer to real application behavior than manually mocking the dashboard.

### Data and behavior

- Two separate, explicitly named synthetic accounts: **Alice** (`user:demo-alice`) and **Bob** (`user:demo-bob`), each initially containing **48** messages.
- Realistic sender headers, dates (recent and over a year old), subject lines, read/unread state, estimated sizes, attachments, newsletter/notification hints, Inbox/Sent/archived flags, starred/important flags and custom labels.
- Scan pagination/progress, current history ID and incremental history-delta updates.
- Supported write actions: **Trash**, **Archive**, **Mark read**, **Mark unread**, **Add label**, **Move to label**, and **Create label**. Their state changes really occur in the dummy mailbox; no fake "success" is returned without changing that state.
- Explicitly injectable test failures: `DemoMailboxStore.SetFailure(userId, messageId, DemoFailure.Rejected)` and `DemoFailure.UncertainAfterApplying`. The latter simulates a network timeout **after** the mutation was applied.
- One-shot scan outage: `DemoMailboxStore.FailNextSync(userId)`; used to verify preservation of the last successful snapshot.
- No Gmail credentials, real email bodies, Google traffic or production mail are involved.

## Enable safely on your machine

Requires .NET 8 SDK. From the repository root, in a terminal:

```bash
ASPNETCORE_ENVIRONMENT=Development \
Mailbox__DemoMode=true \
ASPNETCORE_URLS=http://127.0.0.1:5088 \
dotnet run --project src/MailboxCleaner.Web --no-launch-profile
```

Open [http://127.0.0.1:5088](http://127.0.0.1:5088) and choose **Open demo Alice** or **Open demo Bob**.

- Click **Start scan**, wait for **Continue to dashboard** and inspect the sender overview, filters and cleanup suggestions.
- Select messages, choose an action, review its consequences and confirm. The fake mailbox state and dashboard change as if Gmail accepted the action.
- Use a separate private browser context for Bob; confirm that Alice's changes never show up in Bob's mailbox.
- The demo mailbox and metadata cache are **in-memory only** and reset on application restart. Google OAuth is blocked while demo mode is active.
- Use `/demo/logout` to leave a demo account.
- Never expose a development-mode server on the public internet. Demo login is limited to loopback requests; the mode also uses separate cookie and Data Protection application names.

**Fail-closed safeguards:** `Mailbox:DemoMode` is **false by default**. If it is enabled with `ASPNETCORE_ENVIRONMENT=Production` (or any non-Development environment), the application **refuses to start**. In ordinary mode, the DI registrations keep using `GmailClient`, `SqliteTokenStore` and `SqliteMailboxMetadataStore`. The demo cannot fetch or modify real Gmail.

## Run tests

### .NET integration-style xUnit tests

```bash
dotnet test MailboxCleaner.sln -c Release
```

The tests in `tests/MailboxCleaner.Web.Tests/DemoMailboxIntegrationTests.cs` create real DI scopes with `IGmailClient => DemoGmailClient` and exercise:

- Complete metadata scan and the real dashboard statistics/cleanup suggestion services.
- All important Gmail mutations and the local metadata cache.
- History-ID incremental synchronization and stale cursor fallback.
- Two-account isolation, including a cross-account write attempt.
- Partial failures, rejected IDs, uncertain-after-write behavior and safe targeted retries.
- Failed rescan recovery with the previous successful scan still accessible.

Other project tests continue to cover OAuth/controller contracts, token storage and the real service layer through fakes.

### Chromium browser test (real Blazor interactions)

GitHub Actions builds the .NET app, starts it on localhost with `Mailbox__DemoMode=true`, installs Chromium and runs `tests/browser/demo-mailbox.spec.mjs`. It checks actual browser interactions: cookie login, Blazor Server scan, sender checkbox selection, preview/confirmation, Trash and dashboard count changes, then Bob's isolated mailbox.

To run this manually after starting the local demo server:

```bash
cd tests/browser
npm install
npx playwright install chromium
npx playwright test
```

If it fails, GitHub uploads the Playwright screenshots/traces. The workflow must pass before we claim the simulated full browser flow works.

## What this DOES NOT prove

The dummy provider deliberately mirrors the *expected* Gmail semantics. Tests passing here **cannot prove** Google's actual behavior for OAuth consent, API access, response fields, rate limits, latency, batchModify per-message acknowledgement or expired/revoked credentials.

Those remaining **real Gmail tests** still need a separate Google Cloud OAuth test project and disposable test mailboxes, never production messages. Also keep the browser and multi-account tests as regression checks.

## Technical files

- `src/MailboxCleaner.Web/Infrastructure/Demo/DemoMailboxStore.cs` — synthetic fixtures, per-account state, failure controls.
- `src/MailboxCleaner.Web/Infrastructure/Demo/DemoGmailClient.cs` — full `IGmailClient` simulation, history and mutations.
- `src/MailboxCleaner.Web/Infrastructure/Demo/DemoAuthEndpoints.cs` — isolated development-only cookie login and disabled token store.
- `src/MailboxCleaner.Web/Program.cs` — conditional DI registration and fail-closed security guard.
- `tests/MailboxCleaner.Web.Tests/DemoMailboxIntegrationTests.cs` — DI-based service tests.
- `tests/browser/` — Playwright browser smoke test.
- `.github/workflows/dotnet-ci.yml` — independent .NET and browser jobs.

## Bugs the simulated workflow exposed

- **RV-01 (Trash/cache):** acknowledged Trash actions now remove messages from the active in-memory or SQLite snapshot immediately, matching the real full Gmail scan's exclusion of Trash.
- **RV-02 (failed rescan):** a failed or cancelled scan leaves the last completed snapshot usable, with a visible error and a retry option.

Do not mark either scenario production-verified until confirmed against a real disposable Gmail account.
