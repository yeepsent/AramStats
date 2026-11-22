# AramStats: secure shared-statistics architecture (draft)

## Current constraints
- Desktop client uses Riot's local LCU; cloud service cannot directly trust submitted match records.
- The deployed `sync-matches` Edge Function currently requires a developer-only `x-upload-token` for POST.
- `aram_matches` stores `game_id` (primary key), `queue_id`, `game_date`, `match_data` JSONB, `uploaded_at`.
- This shared upload token **must not** be embedded in a portable EXE.
- Current GET count is unauthenticated; do not add a public endpoint returning full player histories or PUUIDs.

## Proposed release design
1. Each member receives a separate **revocable** access credential via an admin-controlled invite flow. No username/password UI is required, but the device still needs a server-issued identity.
2. Store only a **hash** of each credential in the database; never ship a master token or service-role key in the client.
3. Edge Function authenticates requests against that credential, checks revoked/expiry status, limits request size and frequency, and enforces membership.
4. Only admins can change the roster. Members can read shared records for their own group and upload new candidate matches.
5. Use immutable `game_id` and reject conflicting payloads; log submitter, upload time, and payload digest. A match is *unverified* unless independently corroborated.
6. Restrict cloud reads to authenticated members; paginate results, cap page size, and return only fields required for statistics.
7. Keep local cache as offline-safe source and merge remote matches by game ID. Do not overwrite local detailed records with partial cloud records.
8. Render the current device's PUUID as `나` and others by their shared display names. Never require adding yourself as a friend.
9. Exclude the synthetic `aramstats_test_001` row before computing statistics.
10. Deploy after Windows build, two-device upload/download tests, revocation test, and duplicate/conflict tests.

## Caveat
Per-member credentials control who can upload, but do **not** prove that uploaded data is a real Riot match. For strong integrity, require an authoritative match verification source or mark cloud records as member-submitted/unverified.

## Suggested SQL schema (design only; not deployed)
```sql
create table if not exists public.aram_groups (
  id uuid primary key default gen_random_uuid(),
  name text not null
);
create table if not exists public.aram_members (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.aram_groups(id),
  puuid text not null,
  display_name text not null,
  is_admin boolean not null default false,
  revoked_at timestamptz,
  unique (group_id, puuid)
);
create table if not exists public.aram_device_credentials (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null references public.aram_members(id),
  token_hash text not null unique,
  created_at timestamptz not null default now(),
  expires_at timestamptz,
  revoked_at timestamptz
);
```

All three tables should have RLS enabled and **no anonymous direct grants**. Edge Functions may use a server-only service role credential for narrowly scoped, validated operations. Never execute this migration on the live project without reviewing existing data and access policies.
