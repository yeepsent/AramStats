# Cloud sync v4 verification and release gates

Development artifact: `AramStats_cloud_dev_source_v4.zip` in the current conversation.

## Source-level verification
- Original match-history parsing normalizes seconds to milliseconds.
- The match-detail model has the ten participant fields expected by the deployed Edge Function.
- The development uploader only uploads the newest eligible complete match, after explicit confirmation.
- v4 rejects null participant entries before accessing their properties and excludes timestamps beyond the current time plus 24 hours.
- Ten static checks and ZIP integrity check passed.

## Not yet verified
- Windows .NET 10 WinForms compilation (no .NET SDK in this execution environment).
- Live Supabase POST (no user token or running League client).
- End-to-end cloud download and shared group statistics (server endpoints not implemented).
- Import of full sanitized source into GitHub; the ZIP is not a GitHub commit.

## Security
- The existing `ARAMSTATS_UPLOAD_TOKEN` is only suitable for developer testing.
- Never embed the upload token or `ARAMSTATS_SECRET_KEY` in a distributed executable.
- Exclude the synthetic `aramstats_test_001` database row from real statistics.
- Keep the production `main` branch and Releases unchanged until compilation, authentication, and end-to-end tests pass.
