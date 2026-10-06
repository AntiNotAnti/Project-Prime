import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";

// Execute the actual Edge handler with a local tagged-SQL fixture. No remote
// connection or production credential is loaded by this adapter.
const serverKey = "local-opaque-career-reporter-key-32";
const serverHash = createHash("sha256").update(serverKey).digest("hex");
let handler: (req: Request) => Promise<Response>;
(globalThis as any).Deno = { env: { get: () => "local-fixture" }, serve: (fn: typeof handler) => { handler = fn; } };
(globalThis as any).localCareerSql = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const text = strings.join("?");
  if (text.includes("select server_id")) return values[0] === serverHash ? [{ server_id: "12345678-1234-4234-9234-123456789abc", trust_class: 1, enabled: true }] : [];
  if (text.includes("update public.project_prime_career_reporters")) return [];
  throw new Error("Unexpected SQL in authentication/body regression");
};
registerHooks({ resolve(specifier, context, next) {
  if (specifier.startsWith("npm:postgres")) return { url: "data:text/javascript,export default()=>globalThis.localCareerSql", shortCircuit: true };
  return next(specifier, context);
} });
await import("../../supabase/functions/career-report/index.ts");
const request = (body?: string | ReadableStream<Uint8Array>, key = serverKey) => new Request("http://localhost/functions/v1/career-report", { method: body === undefined ? "GET" : "POST", headers: { authorization: "Bearer " + key }, body, duplex: "half" } as RequestInit);

test("opaque reporter gateway configuration and real handler probe", async () => {
  assert.match(readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8"), /\[functions\.career-report\]\s*(?:#[^\n]*\s*)*verify_jwt\s*=\s*false/);
  const response = await handler(request());assert.equal(response.status, 200);assert.equal((await response.json()).ok, true);
  assert.equal((await handler(request(undefined, "unknown-key-of-at-least-32-characters"))).status, 401);
});
test("unknown reporter rejected before consuming streamed request body", async () => {
  let pulls = 0;
  const stream = new ReadableStream<Uint8Array>({ pull(c) { pulls++; c.enqueue(new Uint8Array(1024)); } }, { highWaterMark: 0 });
  assert.equal((await handler(request(stream, "unknown-key-of-at-least-32-characters"))).status, 401);assert.equal(pulls, 0);
});
test("authenticated streamed body ceiling and JSON object validation", async () => {
  let canceled = false;
  const stream = new ReadableStream<Uint8Array>({ pull(c) { c.enqueue(new Uint8Array(65536)); }, cancel() { canceled = true; } }, { highWaterMark: 0 });
  assert.equal((await handler(request(stream))).status, 413);assert.equal(canceled, true);
  for (const body of ["null", "[]", "{", "{}"] ) assert.equal((await handler(request(body))).status, 400);
});
