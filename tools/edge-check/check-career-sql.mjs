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
