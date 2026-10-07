// Disposable local SQL acceptance, with no production connection or repository dependency install.
// Runtime docs: https://pglite.dev/docs/ and https://pglite.dev/examples (PL/pgSQL).
import { readFile, readdir, mkdtemp, writeFile, rm, realpath } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
const repo = process.argv[2] ?? fileURLToPath(new URL('../../',import.meta.url));
const version = '0.5.8';
const integrity = 'sha512-n9tsbUOhwx2epK1V0ZG9Ar4SHWUju04dhmzZXiSBXwBoleOvIfals33NAaWgagQVAL4Rbvx/Ptsu3P+pA09f6Q==';
const runtime = await realpath(await mkdtemp('/tmp/prime-career-sql-'));
let db;
let phase = 'temporary pinned runtime';
try {
 await writeFile(join(runtime,'package.json'),JSON.stringify({name:'prime-career-sql-runtime',private:true,version:'1.0.0',dependencies:{'@electric-sql/pglite':version}}));
 await writeFile(join(runtime,'user.npmrc'),'');
 await writeFile(join(runtime,'global.npmrc'),'');
 execFileSync('npm',['install','--prefix',runtime,'--ignore-scripts','--no-audit','--no-fund',
  '--registry=https://registry.npmjs.org/','--cache',join(runtime,'npm-cache'),
  '--userconfig',join(runtime,'user.npmrc'),'--globalconfig',join(runtime,'global.npmrc')],{stdio:'inherit',cwd:runtime});
 const lock=JSON.parse(await readFile(join(runtime,'package-lock.json'),'utf8'));
 const pinned=lock.packages['node_modules/@electric-sql/pglite'];
 assert.equal(pinned.version,version);
 assert.equal(pinned.integrity,integrity);
 console.log('PASS isolated runtime pin',version,integrity);
 const {PGlite}=await import(pathToFileURL(join(runtime,'node_modules/@electric-sql/pglite/dist/index.js')).href);
 db = new PGlite({onNotice: notice => console.log('NOTICE', notice.message)});
 phase='runtime';
 console.log(JSON.stringify((await db.query('select version(), current_user')).rows));
 assert((await db.query("select lanname from pg_language where lanname='plpgsql'")).rows.length === 1);
 phase='fixture';
 await db.exec(await readFile(new URL('./sql-fixture/fixture.sql',import.meta.url),'utf8'));
 console.log('PASS disposable compatible prime schema and auth roles');
 for(const file of (await readdir(`${repo}/supabase/migrations`)).filter(x=>x.endsWith('.sql')).sort()) {
  phase=file;
  const sql=await readFile(`${repo}/supabase/migrations/${file}`,'utf8');
  if(file==='20261006163506_career_cumulative_admissions.sql') {
   await db.exec(await readFile(new URL('./sql-fixture/legacy-before.sql',import.meta.url),'utf8'));
   await db.exec(sql);
   await db.exec(await readFile(new URL('./sql-fixture/legacy-after.sql',import.meta.url),'utf8'));
   console.log('PASS pre-upgrade version 1 acceptance, post-upgrade unchanged retry, changed hash/reporter conflicts, bot no-write, all rows/aggregates/balances unchanged, rollback');
  }
  await db.exec(sql);
  console.log('PASS migration', file, createHash('sha256').update(sql).digest('hex'));
 }
 phase='account recovery bridge';
 await db.exec(await readFile(new URL('./sql-fixture/bridge.sql',import.meta.url),'utf8'));
 console.log('PASS actual service/session bridge and recovered profile preservation');
 phase='social foundation, presence and invites';
 const socialA='11111111-1111-4111-8111-111111111111';
 const socialB='22222222-2222-4222-8222-222222222222';
 const socialC='33333333-3333-4333-8333-333333333333';
 await db.query(`select public.project_prime_hunter_license_for('${socialA}'::uuid,'Alpha',0)`);
 await db.query(`select public.project_prime_hunter_license_for('${socialB}'::uuid,'Beta',1)`);
 await db.query(`select public.project_prime_hunter_license_for('${socialC}'::uuid,'Gamma',2)`);
 const socialProfiles=(await db.query(`select player_id,prime_id from prime.social_profiles where player_id in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid) order by player_id`)).rows;
 assert.deepEqual(socialProfiles.map(x=>x.prime_id),['PP-1111-1111-1111-4111-8111','PP-2222-2222-2222-4222-8222','PP-3333-3333-3333-4333-8333']);
 const socialRls=(await db.query(`select c.relname,c.relrowsecurity from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='prime' and c.relname in ('social_profiles','friend_requests','friendships','player_blocks','social_settings','social_presence_sessions','social_lobby_memberships','social_lobbies','game_invites','social_recent_players','social_parties','social_party_members','social_party_invites','social_party_travel','social_party_travel_responses') order by c.relname`)).rows;
 assert.equal(socialRls.length,15); assert.ok(socialRls.every(x=>x.relrowsecurity===true));
 const socialPrivileges=(await db.query(`select
  has_table_privilege('authenticated','prime.social_profiles','select') as profile_select,
  has_table_privilege('authenticated','prime.friend_requests','select') as request_select,
  has_table_privilege('authenticated','prime.social_presence_sessions','select') as presence_select,
  has_table_privilege('authenticated','prime.social_lobby_memberships','select') as membership_select,
  has_table_privilege('authenticated','prime.social_lobbies','select') as lobby_select,
  has_table_privilege('authenticated','prime.game_invites','select') as invite_select,
  has_table_privilege('authenticated','prime.social_recent_players','select') as recent_select,
  has_table_privilege('authenticated','prime.social_parties','select') as party_select,
  has_table_privilege('authenticated','prime.social_party_members','select') as party_member_select,
  has_table_privilege('authenticated','prime.social_party_invites','select') as party_invite_select,
  has_table_privilege('authenticated','prime.social_party_travel','select') as party_travel_select,
  has_table_privilege('authenticated','prime.social_party_travel_responses','select') as party_travel_response_select,
  has_function_privilege('authenticated','prime.social_mutate(uuid,text,text)','execute') as mutate,
  has_function_privilege('authenticated','prime.social_presence_heartbeat(uuid,uuid,text,text,boolean,uuid)','execute') as heartbeat,
  has_function_privilege('authenticated','prime.social_invite_send(uuid,text,uuid)','execute') as invite_send,
  has_function_privilege('authenticated','prime.social_party_action(uuid,text,text,uuid)','execute') as party_action,
  has_function_privilege('authenticated','prime.social_party_travel_publish(uuid,uuid,text)','execute') as party_travel_publish
 `)).rows[0];
 assert.deepEqual(socialPrivileges,{profile_select:false,request_select:false,presence_select:false,membership_select:false,lobby_select:false,invite_select:false,recent_select:false,party_select:false,party_member_select:false,party_invite_select:false,party_travel_select:false,party_travel_response_select:false,mutate:false,heartbeat:false,invite_send:false,party_action:false,party_travel_publish:false});

 let socialResult=(await db.query(`select prime.social_mutate('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','send_request') as value`)).rows[0].value;
 assert.equal(socialResult.ok,true); assert.equal(socialResult.status,'request_sent');
 socialResult=(await db.query(`select prime.social_mutate('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','send_request') as value`)).rows[0].value;
 assert.equal(socialResult.ok,true); assert.equal(socialResult.status,'request_pending');
 let socialSnapshot=(await db.query(`select prime.social_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 assert.deepEqual(socialSnapshot.incoming_requests.map(x=>x.prime_id),['PP-1111-1111-1111-4111-8111']);
 socialResult=(await db.query(`select prime.social_mutate('${socialB}'::uuid,'PP-1111-1111-1111-4111-8111','send_request') as value`)).rows[0].value;
 assert.equal(socialResult.status,'friends'); assert.equal(socialResult.snapshot.friends.length,1);

 const sessionA='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
 const sessionB='bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
 const sessionC='cccccccc-cccc-4ccc-8ccc-cccccccccccc';
 let lobbyResult=(await db.query(`select prime.social_lobby_register('${socialA}'::uuid,'203.0.113.10'::inet,27891,638000000000000000::numeric,34,'MP1 SANCTORUS','Prime Lobby') as value`)).rows[0].value;
 assert.equal(lobbyResult.ok,false); assert.equal(lobbyResult.status,'membership_unverified');
 await db.query(`insert into prime.social_lobby_memberships(player_id,authority_epoch,reporter_id,client_id,lobby_eligible,expires_at) values ('${socialA}'::uuid,638000000000000000::numeric,'99999999-9999-4999-8999-999999999999'::uuid,12345,true,now()+interval '60 seconds')`);
 lobbyResult=(await db.query(`select prime.social_lobby_register('${socialA}'::uuid,'203.0.113.10'::inet,27891,638000000000000000::numeric,34,'MP1 SANCTORUS','Prime Lobby') as value`)).rows[0].value;
 assert.equal(lobbyResult.ok,true); assert.equal(lobbyResult.status,'lobby_registered');
 assert.equal(lobbyResult.lobby.host,'203.0.113.10'); assert.equal(lobbyResult.lobby.port,27891);
 assert.equal(lobbyResult.lobby.authority_epoch,'638000000000000000');
 const socialLobbyId=lobbyResult.lobby.lobby_id;
 assert.match(socialLobbyId,/^[0-9a-f-]{36}$/);

 await db.query(`select prime.social_presence_heartbeat('${socialA}'::uuid,'${sessionA}'::uuid,'lobby','MP1 SANCTORUS',true,'${socialLobbyId}'::uuid)`);
 await db.query(`select prime.social_presence_heartbeat('${socialB}'::uuid,'${sessionB}'::uuid,'menu',null,false,null)`);
 await db.query(`select prime.social_presence_heartbeat('${socialC}'::uuid,'${sessionC}'::uuid,'in_match','MP1 FUEL STACK',false,null)`);
 let presence=(await db.query(`select prime.social_presence_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 let alpha=presence.players.find(x=>x.prime_id==='PP-1111-1111-1111-4111-8111');
 let gamma=presence.players.find(x=>x.prime_id==='PP-3333-3333-3333-4333-8333');
 assert.equal(alpha.activity,'lobby'); assert.equal(alpha.room_key,'MP1 SANCTORUS'); assert.equal(alpha.joinable,true); assert.equal(alpha.lobby_id,socialLobbyId); assert.equal(alpha.is_friend,true);
 assert.equal(gamma.activity,'online'); assert.equal(gamma.room_key,null); assert.equal(gamma.joinable,false); assert.equal(gamma.is_friend,false);

 let inviteResult=(await db.query(`select prime.social_invite_send('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','${socialLobbyId}'::uuid) as value`)).rows[0].value;
 assert.equal(inviteResult.ok,true); assert.equal(inviteResult.status,'invite_sent');
 const socialInviteId=inviteResult.invite_id;
 let inviteSnapshot=(await db.query(`select prime.social_invites_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 assert.equal(inviteSnapshot.incoming.length,1); assert.equal(inviteSnapshot.incoming[0].invite_id,socialInviteId);
 assert.equal(inviteSnapshot.incoming[0].prime_id,'PP-1111-1111-1111-4111-8111');
 inviteResult=(await db.query(`select prime.social_invite_action('${socialB}'::uuid,'${socialInviteId}'::uuid,'accept') as value`)).rows[0].value;
 assert.equal(inviteResult.ok,true); assert.equal(inviteResult.status,'invite_accepted');
 assert.equal(inviteResult.locator.host,'203.0.113.10'); assert.equal(inviteResult.locator.port,27891);
 assert.equal(inviteResult.locator.authority_epoch,'638000000000000000');
 const duplicateLiveInvite=(await db.query(`select prime.social_invite_send('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','${socialLobbyId}'::uuid) as value`)).rows[0].value;
 assert.equal(duplicateLiveInvite.ok,true); assert.equal(duplicateLiveInvite.status,'invite_active'); assert.equal(duplicateLiveInvite.invite_id,socialInviteId);
 assert.equal((await db.query(`select count(*)::int as count from prime.game_invites where sender_id='${socialA}'::uuid and recipient_id='${socialB}'::uuid and lobby_id='${socialLobbyId}'::uuid and status in ('pending','accepted')`)).rows[0].count,1);
 const friendJoin=(await db.query(`select prime.social_join_friend('${socialB}'::uuid,'PP-1111-1111-1111-4111-8111') as value`)).rows[0].value;
 assert.equal(friendJoin.ok,true); assert.equal(friendJoin.status,'friend_resolved'); assert.equal(friendJoin.locator.lobby_id,socialLobbyId);

 const reporter='99999999-9999-4999-8999-999999999999';
 await db.query(`insert into prime.social_lobby_memberships(player_id,authority_epoch,reporter_id,client_id,lobby_eligible,expires_at) values ('${socialB}'::uuid,638000000000000000::numeric,'${reporter}'::uuid,22345,false,now()+interval '60 seconds')`);
 await db.query(`select prime.social_recent_touch('${socialA}'::uuid,638000000000000000::numeric,'${reporter}'::uuid)`);
 let partySnapshot=(await db.query(`select prime.social_party_snapshot('${socialA}'::uuid) as value`)).rows[0].value;
 assert.equal(partySnapshot.recent_players[0].prime_id,'PP-2222-2222-2222-4222-8222'); assert.equal(partySnapshot.recent_players[0].encounters,1);

 presence=(await db.query(`select prime.social_privacy_update('${socialB}'::uuid,'everyone','friends','friends',true) as value`)).rows[0].value;
 assert.equal(presence.settings.do_not_disturb,true);
 let partyResult=(await db.query(`select prime.social_party_action('${socialA}'::uuid,'invite','PP-2222-2222-2222-4222-8222',null) as value`)).rows[0].value;
 assert.equal(partyResult.ok,false); assert.equal(partyResult.status,'do_not_disturb');
 const dndGameInvite=(await db.query(`select prime.social_invite_send('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','${socialLobbyId}'::uuid) as value`)).rows[0].value;
 assert.equal(dndGameInvite.ok,false); assert.equal(dndGameInvite.status,'do_not_disturb');

 await db.query(`select prime.social_privacy_update('${socialB}'::uuid,'everyone','friends','friends',false)`);
 partyResult=(await db.query(`select prime.social_party_action('${socialA}'::uuid,'invite','PP-2222-2222-2222-4222-8222',null) as value`)).rows[0].value;
 assert.equal(partyResult.ok,true); assert.ok(['party_created_and_invited','party_invite_sent'].includes(partyResult.status));
 partySnapshot=(await db.query(`select prime.social_party_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 assert.equal(partySnapshot.incoming_party_invites.length,1);
 const partyInviteId=partySnapshot.incoming_party_invites[0].invite_id;
 partyResult=(await db.query(`select prime.social_party_action('${socialB}'::uuid,'accept',null,'${partyInviteId}'::uuid) as value`)).rows[0].value;
 assert.equal(partyResult.ok,true); assert.equal(partyResult.status,'party_joined'); assert.equal(partyResult.snapshot.party.members.length,2);
 assert.equal(partyResult.snapshot.party.leader_prime_id,'PP-1111-1111-1111-4111-8111');

 let travelResult=(await db.query(`select prime.social_party_travel_publish('${socialB}'::uuid,'${socialLobbyId}'::uuid,'quick_play') as value`)).rows[0].value;
 assert.equal(travelResult.ok,false); assert.equal(travelResult.status,'leader_only');
 travelResult=(await db.query(`select prime.social_party_travel_publish('${socialA}'::uuid,'${socialLobbyId}'::uuid,'quick_play') as value`)).rows[0].value;
 assert.equal(travelResult.ok,true); assert.equal(travelResult.status,'party_travel_published');
 const travelId=travelResult.travel.travel_id;
 const travelRevision=travelResult.travel.revision;
 assert.equal(travelResult.travel.reason,'quick_play'); assert.equal(travelResult.travel.members.length,2);
 let memberTravel=(await db.query(`select prime.social_party_travel_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 assert.equal(memberTravel.travel_id,travelId); assert.equal(memberTravel.self_status,'pending');

 let followResult=(await db.query(`select prime.social_party_travel_respond('${socialB}'::uuid,'${travelId}'::uuid,${travelRevision},'follow') as value`)).rows[0].value;
 assert.equal(followResult.ok,true); assert.equal(followResult.status,'party_travel_following');
 assert.equal(followResult.locator.lobby_id,socialLobbyId);

 const secondEpoch='638000000000000111';
 await db.query(`insert into prime.social_lobby_memberships(player_id,authority_epoch,reporter_id,client_id,lobby_eligible,expires_at) values ('${socialA}'::uuid,${secondEpoch}::numeric,'${reporter}'::uuid,12345,true,now()+interval '60 seconds')`);
 const secondLobby=(await db.query(`select prime.social_lobby_register('${socialA}'::uuid,'203.0.113.11'::inet,27892,${secondEpoch}::numeric,34,'MP3 PROVING GROUND','Party Rally') as value`)).rows[0].value;
 assert.equal(secondLobby.ok,true);
 travelResult=(await db.query(`select prime.social_party_travel_publish('${socialA}'::uuid,'${secondLobby.lobby.lobby_id}'::uuid,'regroup') as value`)).rows[0].value;
 assert.equal(travelResult.ok,true); assert.equal(travelResult.travel.revision,travelRevision+1); assert.equal(travelResult.travel.reason,'regroup');

 const staleTravel=(await db.query(`select prime.social_party_travel_respond('${socialB}'::uuid,'${travelId}'::uuid,${travelRevision},'follow') as value`)).rows[0].value;
 assert.equal(staleTravel.ok,false); assert.equal(staleTravel.status,'party_travel_stale');
 followResult=(await db.query(`select prime.social_party_travel_respond('${socialB}'::uuid,'${travelId}'::uuid,${travelRevision+1},'follow') as value`)).rows[0].value;
 assert.equal(followResult.ok,true); assert.equal(followResult.locator.host,'203.0.113.11');
 let joinedTravel=(await db.query(`select prime.social_party_travel_respond('${socialB}'::uuid,'${travelId}'::uuid,${travelRevision+1},'joined') as value`)).rows[0].value;
 assert.equal(joinedTravel.ok,true); assert.equal(joinedTravel.status,'party_travel_joined');
 memberTravel=(await db.query(`select prime.social_party_travel_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 assert.equal(memberTravel.self_status,'joined');

 presence=(await db.query(`select prime.social_privacy_update('${socialA}'::uuid,'friends','private','nobody',false) as value`)).rows[0].value;
 assert.equal(presence.settings.presence_visibility,'friends'); assert.equal(presence.settings.activity_visibility,'private'); assert.equal(presence.settings.invite_policy,'nobody');
 const strangerView=(await db.query(`select prime.social_presence_snapshot('${socialC}'::uuid) as value`)).rows[0].value;
 assert.equal(strangerView.players.some(x=>x.prime_id==='PP-1111-1111-1111-4111-8111'),false);
 const friendView=(await db.query(`select prime.social_presence_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 alpha=friendView.players.find(x=>x.prime_id==='PP-1111-1111-1111-4111-8111');
 assert.equal(alpha.activity,'online'); assert.equal(alpha.room_key,null); assert.equal(alpha.joinable,false); assert.equal(alpha.lobby_id,null);
 const privateJoin=(await db.query(`select prime.social_join_friend('${socialB}'::uuid,'PP-1111-1111-1111-4111-8111') as value`)).rows[0].value;
 assert.equal(privateJoin.ok,false); assert.equal(privateJoin.status,'not_joinable');

 socialResult=(await db.query(`select prime.social_mutate('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','block_player') as value`)).rows[0].value;
 assert.equal(socialResult.status,'blocked'); assert.equal(socialResult.snapshot.friends.length,0); assert.equal(socialResult.snapshot.blocked.length,1);
 const partyAfterBlock=(await db.query(`select prime.social_party_snapshot('${socialA}'::uuid) as value`)).rows[0].value;
 assert.equal(partyAfterBlock.party.members.length,1); assert.equal(partyAfterBlock.party.members[0].prime_id,'PP-1111-1111-1111-4111-8111');
 const travelAfterBlock=(await db.query(`select prime.social_party_travel_snapshot('${socialA}'::uuid) as value`)).rows[0].value;
 assert.equal(travelAfterBlock,null);
 const inviteStatus=(await db.query(`select status from prime.game_invites where invite_id='${socialInviteId}'::uuid`)).rows[0].status;
 assert.equal(inviteStatus,'cancelled');
 const hidden=(await db.query(`select prime.social_lookup('${socialB}'::uuid,'PP-1111-1111-1111-4111-8111') as value`)).rows[0].value;
 assert.equal(hidden,null);
 assert.equal((await db.query(`select prime.social_presence_leave('${socialA}'::uuid,'${sessionA}'::uuid) as value`)).rows[0].value,true);

 await db.query(`delete from prime.hunter_licenses where "PlayerId" in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid)`);
 await db.query(`delete from prime.player_profiles where "PlayerId" in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid)`);
 await db.query(`delete from prime.players where "Id" in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid)`);
 console.log('PASS social IDs, private grants/RLS, DND, recent players, parties, party travel/regroup, invites, Join Friend, blocking and cleanup');
 phase='service grant';
 const grants=(await db.query(`select rol,
  has_function_privilege(rol,'public.ingest_project_prime_career_match(jsonb,uuid,integer,text,text)','execute') as ingest,
  has_function_privilege(rol,'public.project_prime_hunter_license_for(uuid,text,integer)','execute') as bridge,
  has_table_privilege(rol,'public.project_prime_career_reporters','select') as reporter_select
  from unnest(array['anon','authenticated','service_role']) rol`)).rows;
 assert.deepEqual(grants,[{rol:'anon',ingest:false,bridge:false,reporter_select:false},
  {rol:'authenticated',ingest:false,bridge:false,reporter_select:false},
  {rol:'service_role',ingest:true,bridge:true,reporter_select:true}]);
 console.log('PASS preserved service-only ingestion/bridge/reporter grants',JSON.stringify(grants));
 phase='actual role execution';
 for(const role of ['anon','authenticated']) {
  await db.exec(`set role ${role}`);
  try {
   for(const sql of ["select public.ingest_project_prime_career_match('{\"version\":1,\"contains_bots\":true}'::jsonb,'11111111-1111-1111-1111-111111111111'::uuid,0,repeat('A',64),'{}')",'select * from public.project_prime_career_reporters']) {
    await assert.rejects(db.query(sql),error=>error.code==='42501');
   }
  } finally { await db.exec('reset role'); }
 }
 await db.exec('set role service_role');
 try {
  const result=(await db.query("select public.ingest_project_prime_career_match('{\"version\":1,\"contains_bots\":true}'::jsonb,'11111111-1111-1111-1111-111111111111'::uuid,0,repeat('A',64),'{}') as result")).rows[0].result;
  assert.deepEqual(result,{accepted:false,reason:'BotAssistedMatch'});
  assert.deepEqual((await db.query('select * from public.project_prime_career_reporters')).rows,[]);
 } finally { await db.exec('reset role'); }
 console.log('PASS actual anon/authenticated denied ingestion and reporter SELECT, service-role execution allowed');
 for(const file of ['supabase/tests/bot_assisted_career.sql' ,'tools/edge-check/career-cumulative.sql']) {
  phase=file;
  const sql=await readFile(`${repo}/${file}`,'utf8');
  await db.exec(sql);
  console.log('PASS exact acceptance gate',file,createHash('sha256').update(sql).digest('hex'));
 }
 const remaining=(await db.query('select (select count(*) from prime.accepted_matches) as matches, (select count(*) from prime.career_participations) as participants, (select count(*) from prime.rating_transactions) as ratings, (select count(*) from prime.career_aggregates) as aggregates, (select count(*) from prime.players) as players, (select count(*) from public.project_prime_career_reporters) as reporters')).rows[0];
 assert.deepEqual(remaining,{matches:0,participants:0,ratings:0,aggregates:0,players:0,reporters:0});
 console.log('PASS all acceptance fixtures rolled back',JSON.stringify(remaining));
 console.log('PASS complete local SQL verification');
} catch(error) {
 console.error('FAIL phase',phase, error.message, 'code',error.code, 'detail',error.detail ?? '', 'where',error.where ?? '');
 process.exitCode=1;
} finally {
 if(db) await db.close();
 await rm(runtime,{recursive:true,force:true});
}
