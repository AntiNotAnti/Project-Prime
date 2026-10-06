-- Disposable compatibility fixture, NOT a production EF schema or migration.
-- Columns and conflict keys are those referenced by repository career SQL.
create role anon nologin;
create role authenticated nologin;
create role service_role nologin bypassrls;
create schema auth;
create function auth.uid() returns uuid language sql stable as $$
 select nullif(current_setting('request.jwt.claim.sub',true),'')::uuid
$$;
create schema prime;
create table prime.players (
 "Id" uuid primary key, "UserName" text, "NormalizedUserName" text,
 "Email" text, "NormalizedEmail" text, "EmailConfirmed" boolean not null,
 "PasswordHash" text, "SecurityStamp" text, "ConcurrencyStamp" text,
 "PhoneNumber" text, "PhoneNumberConfirmed" boolean not null,
 "TwoFactorEnabled" boolean not null, "LockoutEnd" timestamptz,
 "LockoutEnabled" boolean not null, "AccessFailedCount" integer not null
);
create table prime.player_profiles (
 "PlayerId" uuid primary key references prime.players("Id"),
 "DisplayName" text not null, "FavoriteHunter" smallint not null
);
create table prime.hunter_licenses (
 "PlayerId" uuid primary key references prime.players("Id"),
 "CreatedAt" timestamptz not null, "RatingPoints" integer not null check("RatingPoints" between 0 and 850)
);
create table prime.accepted_matches (
 "MatchId" uuid primary key, "ProcessingOrder" bigserial unique not null,
 "ServerId" uuid not null, "ServerIncarnation" uuid not null, "PayloadHash" text not null,
 "OriginalReport" bytea not null, "AcceptedAt" timestamptz not null,
 "EndedAt" timestamptz not null, "RoomKey" text not null, "Mode" integer not null,
 "TrustClass" integer not null, "CareerEligible" boolean not null,
 "RatingStatus" text not null, "RatingPolicyVersion" integer not null,
 "RatingIneligibilityReason" integer
);
create table prime.rating_transactions (
 "MatchId" uuid references prime.accepted_matches("MatchId"),
 "PlayerId" uuid references prime.hunter_licenses("PlayerId"),
 "ProcessingOrder" bigint not null, "PointsBefore" integer not null,
 "TierBefore" integer not null, "OpponentCount" integer not null,
 "RawDelta" integer not null, "NormalizedDelta" integer not null,
 "AppliedDelta" integer not null, "PointsAfter" integer not null,
 "TierAfter" integer not null, "PolicyVersion" integer not null,
 primary key("MatchId","PlayerId")
);
create table prime.rating_pair_contributions (
 "MatchId" uuid, "PlayerId" uuid, "OpponentPlayerId" uuid references prime.hunter_licenses("PlayerId"),
 "OpponentPointsBefore" integer not null, "OpponentTierBefore" integer not null,
 "Result" integer not null, "Delta" integer not null,
 primary key("MatchId","PlayerId","OpponentPlayerId"),
 foreign key("MatchId","PlayerId") references prime.rating_transactions("MatchId","PlayerId")
);
create table prime.career_participations (
 "MatchId" uuid references prime.accepted_matches("MatchId"),
 "PlayerId" uuid references prime.hunter_licenses("PlayerId"),
 "ProcessingOrder" bigint not null, "Eligible" boolean not null,
 "Won" boolean not null, "Tied" boolean not null, "Outcome" integer not null,
 "PlayedTicks" bigint not null, "Kills" bigint not null, "Deaths" bigint not null,
 "Assists" bigint not null, "Damage" bigint not null,
 primary key("MatchId","PlayerId")
);
create table prime.career_aggregates (
 "PlayerId" uuid references prime.hunter_licenses("PlayerId"),
 "TrustClass" integer not null, "Dimension" text not null, "Key" text not null,
 "Matches" bigint not null, "Wins" bigint not null, "Ties" bigint not null,
 "Losses" bigint not null, "PlayedTicks" bigint not null, "Kills" bigint not null,
 "Deaths" bigint not null, "Assists" bigint not null, "Damage" bigint not null,
 "OctolithScores" bigint not null, "NodesCaptured" bigint not null,
 "KillsAsPrime" bigint not null, "HeadshotKills" bigint not null,
 "BipedKills" bigint, "AltFormKills" bigint, "LongestKillStreak" bigint not null,
 "CurrentWinStreak" bigint not null, "LongestWinStreak" bigint not null,
 "OutcomeSamples" bigint not null,
 primary key("PlayerId","TrustClass","Dimension","Key")
);
create table prime.player_cosmetic_loadouts (
 player_id uuid references prime.players("Id"), hunter smallint not null,
 skin_key text, armor_effect_key text, death_effect_key text, updated_at timestamptz not null,
 primary key(player_id,hunter)
);
