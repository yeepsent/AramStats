# Development integration status (2026-11-22)

The original Windows Forms source was supplied as a ZIP. It is a .NET 10 project with `Form1.cs`, `Form1.Designer.cs`, `Form1.resx`, `Program.cs`, and `AramStats.csproj`.

A source-only development ZIP has been prepared in the conversation with:
- `Form1.CloudSync.cs`: validates complete queue-2400 matches; exports JSON; optionally sends authenticated POST requests using environment-only `ARAMSTATS_SYNC_URL` and `ARAMSTATS_UPLOAD_TOKEN`.
- `Form1.cs`: adds an advanced-management button for cloud export/upload.
- `.gitignore`: excludes build output, Visual Studio user files and local secrets.
- `CLOUD_SYNC_README.md`: testing and security guidance.

No privileged Supabase secret is embedded in source. Upload requires explicit confirmation. The server still uses a shared token, so this is **developer testing only**. No automatic sync, cloud download, or shared roster is implemented yet. Windows build and live Supabase tests are **not verified**.

The ZIP exists as a conversation artifact, but these source modifications have **not** been imported into GitHub; do not mistake this note for a merged implementation. The main branch and existing Releases remain unchanged.

Next: build on Windows, inspect the exported payload, verify one POST against the deployed function, then replace shared-token authorization before public deployment.
