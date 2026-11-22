# AramStats cloud sync integration notes

Source snapshot: user-provided AramStats.zip, version 1.0.1 (.NET 10 Windows Forms).

## Verified source model
- `Form1.cs`: `StoredMatch` includes `GameId`, `QueueId`, `Timestamp` (milliseconds), and account `Puuid`.
- `MatchDetailRecord` includes `GameId` and ten `MatchParticipantRecord` entries.
- `MatchParticipantRecord` includes `ParticipantId`, `Puuid`, `TeamId`, `ChampionId`, `Win`, `Kills`, `Deaths`, `Assists`.
- `UpdateAllAsync` calls `ConnectAccountAsync`, `FetchMatchesAsync`, and `FillContributionAsync` in sequence.

## Integration precautions
- Do not embed `ARAMSTATS_SECRET_KEY` or `ARAMSTATS_UPLOAD_TOKEN` in distributed executables. The current Edge Function requires a shared upload token and therefore cannot yet be safely enabled for all users.
- Use a separate server authorization design before automatic public-client uploads.
- Do not replace the existing local storage, friends management, Discord features, or GitHub updater.
- Cloud payload should merge `StoredMatch` metadata with matching `MatchDetailRecord` participants by `GameId`.
- Only include queue 2400 matches with ten valid participant records.
- The SQL table contains an earlier synthetic test row `aramstats_test_001`; exclude or delete it before statistics aggregation.
- The ZIP includes local `.user` publishing files; do not commit them. Add a `.gitignore` for `bin/`, `obj/`, `.vs/`, `*.user` and local secrets.

## Next implementation
1. Import sanitized project source into this feature branch.
2. Implement upload and download behind feature flags.
3. Replace shared secret authorization with a safe server-mediated design.
4. Test using a Windows .NET 10 build and a real LCU match.
5. Merge only after validation.
