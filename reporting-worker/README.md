# For Honor recovery report service

This Worker receives user-authorized, sanitized diagnostics and creates public issues only in `j117mw3/ForHonorQuickActions`. It never accepts arbitrary issue bodies, destinations, paths, usernames, or command lines. Client submissions remain untrusted.

## Deployment

The verified deployment is at `https://for-honor-recovery-reports.mrassetto.workers.dev/reports`. Reporting is enabled with GitHub App `5265921`, installed as `170039435` on only `j117mw3/ForHonorQuickActions`. Its private key and source-IP hashing key are Cloudflare secrets. [Synthetic test issue #1](https://github.com/j117mw3/ForHonorQuickActions/issues/1) was created, marked as a test, closed, and reused by repeated Worker and desktop submissions without duplication.

1. Create a private GitHub App for the `j117mw3` account. Enable repository **Issues: Read and write**, with mandatory **Metadata: Read-only**. Disable webhooks, OAuth installation authorization, and device flow; no callback URL is required. Install it on **only ForHonorQuickActions**.
2. Generate its private key. Keep the downloaded PEM outside this repository. Convert it to PKCS#8 if it starts with `BEGIN RSA PRIVATE KEY`. Set `GITHUB_APP_ID` and `GITHUB_INSTALLATION_ID` in `wrangler.jsonc`; these IDs are not secrets.
3. From PowerShell 7, upload secrets without displaying their contents:

   ```powershell
   # Use a real private-key file outside the repository.
   $reportRsa = [Security.Cryptography.RSA]::Create()
   $reportRsa.ImportFromPem([IO.File]::ReadAllText('C:\secure\github-app.pem'))
   $reportPem = "-----BEGIN PRIVATE KEY-----`n" + [Convert]::ToBase64String($reportRsa.ExportPkcs8PrivateKey()) + "`n-----END PRIVATE KEY-----"
   $reportPem | npx.cmd wrangler secret put GITHUB_PRIVATE_KEY
   $reportRsa.Dispose()
   $reportRandom = New-Object byte[] 32
   [Security.Cryptography.RandomNumberGenerator]::Fill($reportRandom)
   [Convert]::ToBase64String($reportRandom) | npx.cmd wrangler secret put IP_HASH_KEY
   Remove-Variable reportPem, reportRandom
   ```

4. Run `npm.cmd ci`, `npm.cmd test`, `npm.cmd run check`, and `npm.cmd run dry-run`. Set `REPORTING_ENABLED=true` and run `npm.cmd run deploy` after installation and secret provisioning.
5. Submit synthetic diagnostics through the production endpoint, mark the resulting issue title `[TEST]`, and explain that no real game hang occurred. Verify that repeating the same incident ID returns the same issue URL. Close the synthetic issue after verification. Check disabled and invalid-body responses separately.
6. Only after live verification, set `IncidentReporting.Endpoint` in the application to the HTTPS report endpoint, build the app, and verify its Send report path.

The private key is stored only as a Cloudflare secret. Do not put it in application source, environment configuration committed to Git, log output, or chat. The GitHub App receives short-lived installation tokens restricted to the configured repository and Issues permission. Revoking its installation or key disables GitHub access.

## Submission behavior

- `POST /reports`: version-1 allowlisted JSON, maximum 32 KiB and 200 events. Returns `200 {status:"sent",issueUrl}` only after confirmed issue creation or reconciliation.
- `400/413/415`: invalid report; `409`: incident ID reused with different contents; `429`: limits; `503`: disabled, unavailable, or an ambiguous submission still being reconciled. No backend exceptions or credentials appear in responses.
- One coordinator serializes creation. New-report limits are three per keyed source-IP hash per UTC hour and 100 globally per UTC day. Definite GitHub rejection permits at most three creation attempts per incident.
- Lost creation responses remain pending. Retry searches repository issues for the incident marker and never blindly recreates the issue. An unresolvable pending incident requires operator investigation; there is intentionally no automatic recreation.
- Hour/day counters expire through an hourly alarm. Rejected records remain for 30 days. Sent and pending records retain small hash/ID/URL tombstones to avoid duplication even on a late retry; no report bodies or raw IPs are stored. Operators should inspect abnormal pending-record growth.
- Workers application observability is disabled. Do not enable request-body or authorization-header logging. Cloudflare and GitHub still process network metadata as service providers; this service does not provide network anonymity.
- Turn `REPORTING_ENABLED=false` and redeploy to stop submissions. The desktop remains functional and can save local diagnostics if the backend is unavailable.

## Local validation

Worker tests mock GitHub and storage; they do not prove live credentials or GitHub availability. The root `tests/run.ps1` checks controlled processes, simulated failures, and renders the WinForms views. No test intentionally creates an EAC kernel hang or stops a real EAC service.
