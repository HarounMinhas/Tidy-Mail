# Debugging the mailbox scan

1. Sign in, open browser developer tools (F12) and choose Console.
2. Filter for `[TidyMail:scan]`. Enable Preserve log if navigating to the dashboard.
3. Press Start scan. Expect the history cursor, ID discovery, attachment search, metadata fetching and local save stages, then completion or failure.
4. On failure, copy the tagged lines only. They contain counts, timestamps, retry attempts and sanitized HTTP statuses/reasons, not messages or tokens.

HTTP 400 indicates rejected request parameters; 401 generally requires signing in again; 403 can indicate permissions or quotas; 429 and transient 5xx are retried with backoff. Rate-limit-related 403 responses are retried too. Incomplete snapshots are not committed.

The metadata projection uses only id, threadId, labelIds, sizeEstimate and selected headers; attachment flags are derived from a separate has:attachment query. No body or MIME parts are requested.

The scan screen is responsive and includes a diagnostics disclosure. Broader dashboard redesign remains separate. The real Gmail path still needs validation with an authorized test account; demo-mode CI does not establish real Gmail success.
