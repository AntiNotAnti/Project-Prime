/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import postgres from "npm:postgres@3.4.7";

const enc = new TextEncoder();
const dec = new TextDecoder();
const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, {
  prepare: false,
  max: 1,
  idle_timeout: 20,
});
const epochPattern = /^[1-9][0-9]{0,19}$/;
const maxUint64 = 18446744073709551615n;
const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function json(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function fromB64url(value: string) {
  const padded = value.replaceAll("-", "+").replaceAll("_", "/")
    + "=".repeat((4 - value.length % 4) % 4);
  const binary = atob(padded);
  const result = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) result[i] = binary.charCodeAt(i);
  return result;
}

function hex(bytes: Uint8Array) {
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function sha256(text: string) {
  return hex(new Uint8Array(await crypto.subtle.digest("SHA-256", enc.encode(text))));
}

async function ticketKey() {
  const service = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!service) throw new Error("Supabase service role is unavailable");
  return await crypto.subtle.importKey(
    "raw",
    enc.encode("project-prime-career-ticket:v1:" + service),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["verify"],
  );
}

type Ticket = { v: number; sub: string; cid: number; iat: number; exp: number };

async function verifyTicket(token: unknown, clientId: number): Promise<string | null> {
  if (typeof token !== "string" || token.length > 768) return null;
  const parts = token.split(".");
  if (parts.length !== 3 || parts[0] !== "pp1") return null;
  try {
    const payloadBytes = fromB64url(parts[1]);
    const signature = fromB64url(parts[2]);
    const key = await ticketKey();
    if (!await crypto.subtle.verify("HMAC", key, signature, enc.encode(parts[1])))
      return null;
    const payload = JSON.parse(dec.decode(payloadBytes)) as Ticket;
    const now = Math.floor(Date.now() / 1000);
    if (payload.v !== 1 || payload.cid !== clientId
      || !Number.isInteger(payload.iat) || !Number.isInteger(payload.exp)
      || payload.iat > now + 300 || payload.exp < now - 60
      || payload.exp < payload.iat || payload.exp - payload.iat > 2 * 60 * 60
      || typeof payload.sub !== "string" || !uuidPattern.test(payload.sub)) {
      return null;
    }
    return payload.sub.toLowerCase();
  } catch {
    return null;
  }
}

function validEpoch(value: string) {
  if (!epochPattern.test(value)) return false;
  try {
    const parsed = BigInt(value);
    return parsed > 0n && parsed <= maxUint64;
  } catch {
    return false;
  }
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST" && req.method !== "GET")
    return json(405, { error: "method_not_allowed" });

  const authorization = req.headers.get("authorization") ?? "";
  if (!authorization.startsWith("Bearer "))
    return json(401, { error: "server_auth_required" });
  const serverKey = authorization.slice(7).trim();
  if (serverKey.length < 32 || serverKey.length > 256)
    return json(401, { error: "invalid_server_key" });

  let reporter: { server_id: string; enabled: boolean } | undefined;
  try {
    const serverHash = await sha256(serverKey);
    const rows = await sql`
      select server_id, enabled
      from public.project_prime_career_reporters
      where key_hash = ${serverHash}
      limit 1
    `;
    reporter = rows[0] as typeof reporter;
  } catch (error) {
    console.error("social lobby reporter lookup failed", error);
    return json(500, { error: "reporter_lookup_failed" });
  }
  if (!reporter?.enabled)
    return json(401, { error: "unknown_server" });

  if (req.method === "GET")
    return json(200, { ok: true, server_id: reporter.server_id });

  let body: {
    action?: string;
    authority_epoch?: string;
    client_id?: number;
    career_ticket?: string;
  };
  try {
    body = await readObjectBounded(req, 4096) as typeof body;
  } catch (error) {
    return json(error instanceof RequestBodyError ? error.status : 400, {
      error: "invalid_json",
    });
  }

  const action = body.action === "leave" ? "leave"
    : body.action === "heartbeat" ? "heartbeat" : "";
  const epoch = typeof body.authority_epoch === "string"
    ? body.authority_epoch.trim() : "";
  const clientId = Number.isInteger(body.client_id) ? body.client_id! : 0;
  if (!action || !validEpoch(epoch) || clientId < 1 || clientId > 0xffffffff)
    return json(400, { error: "invalid_membership" });

  const playerId = await verifyTicket(body.career_ticket, clientId);
  if (!playerId)
    return json(401, { error: "invalid_career_ticket" });

  try {
    if (action === "leave") {
      await sql`
        delete from prime.social_lobby_memberships
        where player_id = ${playerId}::uuid
          and authority_epoch = ${epoch}::numeric
          and reporter_id = ${reporter.server_id}::uuid
      `;
      return json(200, { ok: true, status: "membership_removed" });
    }

    await sql`
      insert into prime.social_lobby_memberships (
        player_id, authority_epoch, reporter_id, client_id, updated_at, expires_at
      )
      values (
        ${playerId}::uuid, ${epoch}::numeric, ${reporter.server_id}::uuid,
        ${clientId}::bigint, now(), now() + interval '60 seconds'
      )
      on conflict (player_id, authority_epoch, reporter_id) do update
      set client_id = excluded.client_id,
          updated_at = now(),
          expires_at = now() + interval '60 seconds'
    `;
    await sql`
      delete from prime.social_lobby_memberships
      where expires_at < now() - interval '10 minutes'
    `;
    return json(200, {
      ok: true,
      status: "membership_verified",
      player_id: playerId,
    });
  } catch (error) {
    console.error("social lobby membership write failed", error);
    return json(500, { error: "membership_db_failed" });
  }
});
