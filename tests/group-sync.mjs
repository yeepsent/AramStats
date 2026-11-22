import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { PGlite } from '@electric-sql/pglite';

const db = new PGlite();
await db.exec(`
  create schema auth;
  create table auth.users(id uuid primary key);
  create role anon;
  create role authenticated;
  create function auth.uid() returns uuid language sql stable as
      $$select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid$$;
`);
await db.exec(readFileSync(new URL('../supabase/migrations/202611220001_group_sync.sql', import.meta.url), 'utf8'));

const group = '00000000-0000-0000-0000-000000000001';
const other = '00000000-0000-0000-0000-000000000002';
const ids = Array.from({ length: 5 }, (_, i) => '10000000-0000-0000-0000-00000000000' + (i + 1));
const puuid = i => 'test-puuid-' + i;
await db.query('insert into public.aramstats_groups(id, name) values ($1, \'Friends\'), ($2, \'Other\')', [group, other]);
for (const id of ids) await db.query('insert into auth.users values ($1)', [id]);
async function addMember(index, memberGroup, puuidIndex) {
  await db.query('insert into public.aramstats_members(id, group_id, puuid, display_name) values ($1, $2, $3, $4)',
    [ids[index], memberGroup, puuid(puuidIndex), 'Member ' + index]);
}
await addMember(0, group, 0);
await addMember(1, group, 1);
await addMember(2, other, 0);
await addMember(4, group, 20);

async function useMember(index) {
  await db.exec('reset role; set role authenticated');
  await db.query("select set_config('request.jwt.claim.sub', $1, false)", [ids[index]]);
}
async function rpc(sql, ...args) {
  return (await db.query('select public.' + sql + ' as result', args)).rows[0].result;
}
const makeGame = n => ({
  gameId: String(n), queueId: 2400, timestamp: 1750000000000,
  participants: Array.from({ length: 10 }, (_, i) => ({
    puuid: puuid(i), teamId: i < 5 ? 100 : 200, championId: i + 1, win: i < 5,
    kills: i, deaths: 1, assists: 2, name: 'private Riot name'
  }))
});
const upload = (g, ...games) => rpc('aramstats_upload($1, $2)', g, JSON.stringify(games));
const download = (g, after = 0) => rpc('aramstats_download($1, $2)', g, after);
const roster = () => rpc('aramstats_roster()');

await db.exec('set role anon');
await assert.rejects(roster(), { code: '42501' });
await assert.rejects(download(group), { code: '42501' });
await assert.rejects(upload(group, makeGame(42)), { code: '42501' });
await useMember(3);
await assert.rejects(roster(), { code: '42501' });
await useMember(0);
assert.equal((await roster()).members.length, 3);
await assert.rejects(db.query('select * from public.aramstats_group_matches'), { code: '42501' });
await assert.rejects(upload(group, ...Array.from({length: 26}, (_, i) => makeGame(i + 100))), {code: '22023'});
const malformed = makeGame(42);
delete malformed.participants[0].win;
const inconsistent = makeGame(43);
inconsistent.participants[0].win = false;
const wrongQueue = makeGame(44);
wrongQueue.queueId = 450;
const future = makeGame(45);
future.timestamp = Date.now() + 100_000_000;
for (const invalid of [malformed, inconsistent, wrongQueue, future, { ...makeGame(42), gameId: 'aramstats_test_001' }])
  assert.equal((await upload(group, invalid))[0].status, 'invalid');
const game = makeGame(54);
assert.equal((await upload(group, game))[0].status, 'inserted');

await useMember(4);
assert.equal((await upload(group, game))[0].status, 'invalid');
await useMember(1);
const copy = structuredClone(game);
copy.participants.reverse();
copy.participants[0].name = 'changed';
assert.equal((await upload(group, copy))[0].status, 'duplicate');
copy.participants[0].kills += 1;
assert.equal((await upload(group, copy))[0].status, 'conflict');
const [first] = await download(group);
assert.equal(first.members.length, 2);
assert.deepEqual(first.members.map(p => p.memberId), ids.slice(0, 2));
assert.equal(first.members[0].kills, 0);
assert.doesNotMatch(JSON.stringify(first), /puuid|private Riot name/);
assert.deepEqual(await download(group, first.cursor), []);

await useMember(2);
await assert.rejects(download(group), { code: '42501' });
await assert.rejects(upload(group, game), { code: '42501' });
assert.deepEqual(await download(other), []);
assert.equal((await upload(other, game))[0].status, 'inserted');

await db.exec('reset role');
await addMember(3, group, 2);
await useMember(3);
assert.equal((await download(group))[0].members.length, 3);
await db.exec('reset role');
await db.query('update public.aramstats_members set revoked_at = now() where id = $1', [ids[1]]);
await useMember(1);
await assert.rejects(roster(), { code: '42501' });
await assert.rejects(upload(group, game), { code: '42501' });
await assert.rejects(download(group), { code: '42501' });

await useMember(0);
for (let start = 1000; start < 1200; start += 25)
  assert.ok((await upload(group, ...Array.from({length: 25}, (_, i) => makeGame(start + i))))
    .every(result => result.status === 'inserted'));
const page = await download(group);
assert.equal(page.length, 200);
assert.equal(page[0].members.length, 2);
const last = await download(group, page.at(-1).cursor);
assert.equal(last.length, 1);
assert.equal(last[0].gameId, '1199');
assert.deepEqual(await download(group, last[0].cursor), []);
await db.close();
console.log('Group sync SQL passed: access, validation, deduplication, conflicting uploads, privacy, group isolation, roster changes, revocation, and pagination.');
