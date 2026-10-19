-- Disposable compatibility fixture, not production schema or an auth emulator.
-- These are the only predecessor tables referenced by the Social migrations.
-- Their keys/columns come from the actual Hunter License predecessor SQL.
create role anon nologin;
create role authenticated nologin;
create role service_role nologin bypassrls;
create schema auth;
create schema prime;
create schema realtime;
create function auth.uid() returns uuid language sql stable as $$
  select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid
$$;
create table prime.players ("Id" uuid primary key);
create table prime.player_profiles (
  "PlayerId" uuid primary key references prime.players("Id"),
  "DisplayName" text not null,
  "FavoriteHunter" smallint not null default 0
);
-- No fake realtime.send/messages or reporter functions: migrations retain their
-- actual conditional handling when those Supabase services are absent.
