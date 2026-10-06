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
 phase='social foundation and presence';
 const socialA='11111111-1111-4111-8111-111111111111';
 const socialB='22222222-2222-4222-8222-222222222222';
 const socialC='33333333-3333-4333-8333-333333333333';
 await db.query(`select public.project_prime_hunter_license_for('${socialA}'::uuid,'Alpha',0)`);
 await db.query(`select public.project_prime_hunter_license_for('${socialB}'::uuid,'Beta',1)`);
 await db.query(`select public.project_prime_hunter_license_for('${socialC}'::uuid,'Gamma',2)`);
 const socialProfiles=(await db.query(`select player_id,prime_id from prime.social_profiles where player_id in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid) order by player_id`)).rows;
 assert.deepEqual(socialProfiles.map(x=>x.prime_id),['PP-1111-1111-1111-4111-8111','PP-2222-2222-2222-4222-8222','PP-3333-3333-3333-4333-8333']);
 const socialRls=(await db.query(`select c.relname,c.relrowsecurity from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='prime' and c.relname in ('social_profiles','friend_requests','friendships','player_blocks','social_settings','social_presence_sessions') order by c.relname`)).rows;
 assert.equal(socialRls.length,6); assert.ok(socialRls.every(x=>x.relrowsecurity===true));
 const socialPrivileges=(await db.query(`select
  has_table_privilege('authenticated','prime.social_profiles','select') as profile_select,
  has_table_privilege('authenticated','prime.friend_requests','select') as request_select,
  has_table_privilege('authenticated','prime.social_presence_sessions','select') as presence_select,
  has_function_privilege('authenticated','prime.social_mutate(uuid,text,text)','execute') as mutate,
  has_function_privilege('authenticated','prime.social_presence_heartbeat(uuid,uuid,text,text,boolean)','execute') as heartbeat
 `)).rows[0];
 assert.deepEqual(socialPrivileges,{profile_select:false,request_select:false,presence_select:false,mutate:false,heartbeat:false});
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
 await db.query(`select prime.social_presence_heartbeat('${socialA}'::uuid,'${sessionA}'::uuid,'lobby','MP1 SANCTORUS',false)`);
 await db.query(`select prime.social_presence_heartbeat('${socialB}'::uuid,'${sessionB}'::uuid,'menu',null,false)`);
 await db.query(`select prime.social_presence_heartbeat('${socialC}'::uuid,'${sessionC}'::uuid,'in_match','MP1 FUEL STACK',false)`);
 let presence=(await db.query(`select prime.social_presence_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 let alpha=presence.players.find(x=>x.prime_id==='PP-1111-1111-1111-4111-8111');
 let gamma=presence.players.find(x=>x.prime_id==='PP-3333-3333-3333-4333-8333');
 assert.equal(alpha.activity,'lobby'); assert.equal(alpha.room_key,'MP1 SANCTORUS'); assert.equal(alpha.joinable,false); assert.equal(alpha.is_friend,true);
 assert.equal(gamma.activity,'online'); assert.equal(gamma.room_key,null); assert.equal(gamma.joinable,false); assert.equal(gamma.is_friend,false);

 presence=(await db.query(`select prime.social_privacy_update('${socialA}'::uuid,'friends','private','nobody') as value`)).rows[0].value;
 assert.equal(presence.settings.presence_visibility,'friends'); assert.equal(presence.settings.activity_visibility,'private'); assert.equal(presence.settings.invite_policy,'nobody');
 const strangerView=(await db.query(`select prime.social_presence_snapshot('${socialC}'::uuid) as value`)).rows[0].value;
 assert.equal(strangerView.players.some(x=>x.prime_id==='PP-1111-1111-1111-4111-8111'),false);
 const friendView=(await db.query(`select prime.social_presence_snapshot('${socialB}'::uuid) as value`)).rows[0].value;
 alpha=friendView.players.find(x=>x.prime_id==='PP-1111-1111-1111-4111-8111');
 assert.equal(alpha.activity,'online'); assert.equal(alpha.room_key,null); assert.equal(alpha.joinable,false);
 assert.equal((await db.query(`select prime.social_presence_leave('${socialA}'::uuid,'${sessionA}'::uuid) as value`)).rows[0].value,true);
 assert.equal((await db.query(`select prime.social_presence_snapshot('${socialB}'::uuid) as value`)).rows[0].value.players.some(x=>x.prime_id==='PP-1111-1111-1111-4111-8111'),false);

 socialResult=(await db.query(`select prime.social_mutate('${socialA}'::uuid,'PP-2222-2222-2222-4222-8222','block_player') as value`)).rows[0].value;
 assert.equal(socialResult.status,'blocked'); assert.equal(socialResult.snapshot.friends.length,0); assert.equal(socialResult.snapshot.blocked.length,1);
 const hidden=(await db.query(`select prime.social_lookup('${socialB}'::uuid,'PP-1111-1111-1111-4111-8111') as value`)).rows[0].value;
 assert.equal(hidden,null);
 await db.query(`delete from prime.hunter_licenses where "PlayerId" in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid)`);
 await db.query(`delete from prime.player_profiles where "PlayerId" in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid)`);
 await db.query(`delete from prime.players where "Id" in ('${socialA}'::uuid,'${socialB}'::uuid,'${socialC}'::uuid)`);
 console.log('PASS social IDs, private grants/RLS, presence privacy, crossed-request friendship, blocking and cleanup');
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
