import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import postgres from "npm:postgres@3.4.7";
import { InvalidLeaderboardQuery, parseLeaderboardRequest } from "./contract.ts";

// Read-only public projection. DB credentials remain exclusively in the Edge
// Function environment; no auth UUID, email or private match history leaves it.
const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, {
  prepare: false,
  max: 2,
  idle_timeout: 20,
  connect_timeout: 7,
});

function respond(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": status === 200 ? "public, max-age=60, s-maxage=60" : "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

Deno.serve(async (req: Request) => {
  if (req.method !== "GET") return respond(405, { error: "method_not_allowed" });
  let query: ReturnType<typeof parseLeaderboardRequest>;
  try { query = parseLeaderboardRequest(new URL(req.url)); }
  catch (error) {
    if (error instanceof InvalidLeaderboardQuery)
      return respond(400, { error: error.message });
    return respond(400, { error: "invalid_request" });
  }

  try {
    if (query.hunter) {
      // An individual public Hunter card only exists after verified opt-in.
      // The opaque ID is not the Supabase/Auth UUID.
      const profiles = await sql`
        SELECT
          md5('project-prime:public-hunter:v1:' || a."PlayerId"::text) AS public_id,
          p."DisplayName" AS display_name,
          p."FavoriteHunter" AS favorite_hunter,
          a."Matches" AS matches, a."Wins" AS wins,
          a."Losses" AS losses, a."Ties" AS ties,
          a."Kills" AS kills, a."Deaths" AS deaths,
          a."Assists" AS assists, a."Damage" AS damage,
          a."HeadshotKills" AS headshots,
          a."OctolithScores" AS octolith_scores,
          a."NodesCaptured" AS nodes_captured,
          round(100.0*a."Wins"/greatest(a."Matches",1),1)::double precision AS win_rate,
          round(a."Kills"::numeric/greatest(a."Deaths",1),2)::double precision AS kd
        FROM prime.career_aggregates a
        JOIN prime.player_profiles p ON p."PlayerId" = a."PlayerId"
        JOIN prime.hunter_public_visibility visibility
          ON visibility.player_id = a."PlayerId" AND visibility.is_public = true
        WHERE a."TrustClass"=-1 AND a."Dimension"='career' AND a."Key"='all'
          AND a."Matches">0
          AND md5('project-prime:public-hunter:v1:' || a."PlayerId"::text)
            = ${query.hunter}
        LIMIT 1
      `;
      if (!profiles.length) return respond(404, { error: "hunter_not_public" });
      return respond(200, profiles[0]);
    }

    const dimension = query.mode === "all" ? "career" : "mode";
    const bucket = query.mode === "all" ? "all" : query.mode;
    const metric = query.metric;
    const search = "%" + query.search + "%";
    const pageStart = (query.page - 1) * query.pageSize;
    const pageEnd = pageStart + query.pageSize;

    // Trusted projection is populated by server-validated career reports.
    // Class -1 excludes bot-assisted, local and otherwise ineligible matches.
    const results = await sql`
      WITH eligible AS (
        SELECT
          a."PlayerId" AS player_id,
          coalesce(v.is_public,false) AS is_public,
          p."DisplayName" AS public_name,
          a."Matches" AS matches, a."Wins" AS wins,
          a."Losses" AS losses, a."Ties" AS ties,
          a."Kills" AS kills, a."Deaths" AS deaths,
          a."Assists" AS assists, a."HeadshotKills" AS headshots,
          round(100.0*a."Wins"/greatest(a."Matches",1),1)::double precision AS win_rate,
          round(a."Kills"::numeric/greatest(a."Deaths",1),2)::double precision AS kd
        FROM prime.career_aggregates a
        JOIN prime.player_profiles p ON p."PlayerId"=a."PlayerId"
        LEFT JOIN prime.hunter_public_visibility v ON v.player_id=a."PlayerId"
        WHERE a."TrustClass"=-1
          AND a."Dimension"=${dimension} AND a."Key"=${bucket}
          AND a."Matches">0
          AND (NOT ${query.onlyPublic}::boolean OR coalesce(v.is_public,false))
          AND (${query.search}::text='' OR (
             coalesce(v.is_public,false) AND p."DisplayName" ILIKE ${search}
          ))
      ),
      scored AS (
        SELECT
          CASE WHEN is_public THEN
            md5('project-prime:public-hunter:v1:' || player_id::text)
            ELSE NULL END AS public_id,
          CASE WHEN is_public THEN public_name ELSE 'Private Hunter' END AS display_name,
          md5('project-prime:sort:v1:' || player_id::text) AS sort_token,
          is_public, matches, wins, losses, ties, kills, deaths,
          assists, headshots, win_rate, kd
        FROM eligible
      ),
      ranked AS (
        SELECT row_number() OVER (ORDER BY
          CASE WHEN ${metric}::text='wins' THEN wins::double precision END DESC NULLS LAST,
          CASE WHEN ${metric}::text='kills' THEN kills::double precision END DESC NULLS LAST,
          CASE WHEN ${metric}::text='matches' THEN matches::double precision END DESC NULLS LAST,
          CASE WHEN ${metric}::text='kd' THEN kd END DESC NULLS LAST,
          CASE WHEN ${metric}::text='win_rate' THEN win_rate END DESC NULLS LAST,
          CASE WHEN ${metric}::text='headshots' THEN headshots::double precision END DESC NULLS LAST,
          wins DESC, kills DESC, sort_token ASC
        )::int AS rank,
        public_id, display_name, is_public, matches, wins, losses, ties,
        kills, deaths, assists, headshots, win_rate, kd
        FROM scored
      )
      SELECT
        (SELECT count(*)::int FROM eligible) AS total,
        coalesce((
          SELECT jsonb_agg(to_jsonb(page_result) ORDER BY page_result.rank)
          FROM (
            SELECT rank, public_id, display_name, is_public,
                   matches, wins, losses, ties, kills, deaths,
                   assists, headshots, win_rate, kd
            FROM ranked
            WHERE rank>${pageStart} AND rank<=${pageEnd}
            ORDER BY rank
          ) page_result
        ),'[]'::jsonb) AS entries
    `;
    const rated = await sql`
      SELECT EXISTS(SELECT 1 FROM prime.rating_transactions LIMIT 1)
        AS rated_matches_exist
    `;
    const entries: unknown = results[0]?.entries;
    return respond(200, {
      version: 1,
      source: "trusted_career",
      metric,
      mode: query.mode,
      page: query.page,
      page_size: query.pageSize,
      total: results[0]?.total ?? 0,
      ranked_available: rated[0]?.rated_matches_exist === true,
      entries: Array.isArray(entries) ? entries : [],
    });
  } catch (error) {
    console.error("public career query failed", error instanceof Error ? error.name : "unknown");
    return respond(503, { error: "career_service_unavailable" });
  }
});
