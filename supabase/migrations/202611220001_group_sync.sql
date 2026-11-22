begin;

create table public.aramstats_groups (
    id uuid primary key default gen_random_uuid(),
    name text not null check (char_length(btrim(name)) between 1 and 100)
);

create table public.aramstats_members (
    id uuid primary key references auth.users(id),
    group_id uuid not null references public.aramstats_groups(id),
    puuid text not null check (char_length(puuid) between 10 and 200),
    display_name text not null check (char_length(btrim(display_name)) between 1 and 100),
    revoked_at timestamptz,
    unique (group_id, puuid)
);

create table public.aramstats_group_matches (
    sync_id bigint generated always as identity primary key,
    group_id uuid not null references public.aramstats_groups(id),
    game_id text not null check (game_id ~ '^[1-9][0-9]{0,19}$'),
    played_at_ms bigint not null,
    participants jsonb not null,
    submitted_by uuid not null references public.aramstats_members(id),
    submitted_at timestamptz not null default now(),
    unique (group_id, game_id)
);
create index aramstats_group_matches_cursor on public.aramstats_group_matches (group_id, sync_id);

alter table public.aramstats_groups enable row level security;
alter table public.aramstats_members enable row level security;
alter table public.aramstats_group_matches enable row level security;
revoke all on public.aramstats_groups, public.aramstats_members, public.aramstats_group_matches
    from public, anon, authenticated;

create function public.aramstats_roster()
returns jsonb language plpgsql stable security definer set search_path = '' as $$
declare
    caller public.aramstats_members;
begin
    select * into caller from public.aramstats_members where id = auth.uid() and revoked_at is null;
    if not found then raise exception 'Group membership required' using errcode = '42501'; end if;

    return jsonb_build_object(
        'groupId', caller.group_id,
        'groupName', (select name from public.aramstats_groups where id = caller.group_id),
        'ownId', caller.id,
        'ownPuuid', caller.puuid,
        'members', (select jsonb_agg(jsonb_build_object('id', id, 'displayName', display_name) order by id)
                    from public.aramstats_members where group_id = caller.group_id and revoked_at is null)
    );
end;
$$;

create function public.aramstats_download(p_group uuid, p_after bigint default 0)
returns jsonb language plpgsql stable security definer set search_path = '' as $$
declare
    caller_group uuid;
begin
    select group_id into caller_group from public.aramstats_members where id = auth.uid() and revoked_at is null;
    if not found or caller_group is distinct from p_group then
        raise exception 'Group membership required' using errcode = '42501';
    end if;

    return (
        select coalesce(jsonb_agg(jsonb_build_object(
            'cursor', game.sync_id, 'gameId', game.game_id, 'timestamp', game.played_at_ms,
            'members', (
                select coalesce(jsonb_agg((entry - 'puuid') || jsonb_build_object('memberId', member.id)), '[]'::jsonb)
                from jsonb_array_elements(game.participants) as row(entry)
                join public.aramstats_members member
                  on member.group_id = caller_group and member.puuid = entry->>'puuid' and member.revoked_at is null
            )
        ) order by game.sync_id), '[]'::jsonb)
        from (
            select * from public.aramstats_group_matches
            where group_id = caller_group and sync_id > greatest(p_after, 0)
            order by sync_id limit 200
        ) game
    );
end;
$$;

create function public.aramstats_upload(p_group uuid, p_matches jsonb)
returns jsonb language plpgsql volatile security definer set search_path = '' as $$
declare
    caller public.aramstats_members;
    game jsonb;
    game_key text;
    played_at bigint;
    people jsonb;
    existing public.aramstats_group_matches;
    outcome text;
    results jsonb := '[]'::jsonb;
begin
    select * into caller from public.aramstats_members where id = auth.uid() and revoked_at is null;
    if not found or caller.group_id is distinct from p_group then
        raise exception 'Group membership required' using errcode = '42501';
    end if;
    if jsonb_typeof(p_matches) is distinct from 'array' then
        raise exception 'Expected a match array' using errcode = '22023';
    end if;
    if octet_length(p_matches::text) > 200000 or jsonb_array_length(p_matches) not between 1 and 25 then
        raise exception 'Batch size exceeded' using errcode = '22023';
    end if;

    -- Same-group insert order must match commit order so a download cursor cannot skip a late commit.
    perform pg_advisory_xact_lock(hashtextextended(caller.group_id::text, 0));
    for game in select value from jsonb_array_elements(p_matches) loop
        game_key := game->>'gameId';
        outcome := 'invalid';
        if jsonb_typeof(game->'gameId') = 'string' and game_key ~ '^[1-9][0-9]{0,19}$'
            and game->'queueId' = '2400'::jsonb
            and jsonb_typeof(game->'timestamp') = 'number'
            and game->>'timestamp' ~ '^[0-9]{13}$'
            and jsonb_typeof(game->'participants') = 'array'
        then
            played_at := (game->>'timestamp')::bigint;
            if played_at between 1500000000000 and (extract(epoch from now()) * 1000)::bigint + 86400000
                and jsonb_array_length(game->'participants') = 10
                and not exists (
                    select from jsonb_array_elements(game->'participants') p
                    where jsonb_typeof(p->'puuid') is distinct from 'string'
                        or char_length(btrim(p->>'puuid')) not between 10 and 200
                        or jsonb_typeof(p->'teamId') is distinct from 'number'
                        or p->>'teamId' not in ('100', '200')
                        or jsonb_typeof(p->'championId') is distinct from 'number'
                        or p->>'championId' !~ '^[1-9][0-9]{0,8}$'
                        or jsonb_typeof(p->'win') is distinct from 'boolean'
                        or exists (
                            select from unnest(array['kills', 'deaths', 'assists']) as keys(field)
                            where jsonb_typeof(p->field) is distinct from 'number'
                                or p->>field !~ '^(0|[1-9][0-9]{0,2}|1000)$'
                        )
                )
                and (
                    select count(distinct p->>'puuid') = 10
                        and count(*) filter (where p->>'teamId' = '100') = 5
                        and count(distinct ((p->>'teamId' = '100') = (p->>'win' = 'true'))) = 1
                        and bool_or(p->>'puuid' = caller.puuid)
                    from jsonb_array_elements(game->'participants') p
                )
            then
                select jsonb_agg(jsonb_build_object(
                    'puuid', p->'puuid', 'teamId', p->'teamId', 'championId', p->'championId',
                    'win', p->'win', 'kills', p->'kills', 'deaths', p->'deaths', 'assists', p->'assists'
                ) order by (p->>'puuid') collate "C")
                into people from jsonb_array_elements(game->'participants') p;

                select * into existing from public.aramstats_group_matches
                    where group_id = caller.group_id and game_id = game_key;
                if not found then
                    insert into public.aramstats_group_matches
                        (group_id, game_id, played_at_ms, participants, submitted_by)
                    values (caller.group_id, game_key, played_at, people, caller.id);
                    outcome := 'inserted';
                elsif existing.played_at_ms = played_at and existing.participants = people then
                    outcome := 'duplicate';
                else
                    outcome := 'conflict';
                end if;
            end if;
        end if;
        results := results || jsonb_build_array(jsonb_build_object('gameId', game_key, 'status', outcome));
    end loop;
    return results;
end;
$$;

revoke all on function public.aramstats_roster(), public.aramstats_download(uuid, bigint), public.aramstats_upload(uuid, jsonb)
    from public, anon, authenticated;
grant execute on function public.aramstats_roster(), public.aramstats_download(uuid, bigint), public.aramstats_upload(uuid, jsonb)
    to authenticated;

commit;
