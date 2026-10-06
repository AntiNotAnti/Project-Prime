import { strict as assert } from "node:assert";
import { test } from "node:test";
import { readObjectBounded, RequestBodyError } from "../../supabase/functions/_shared/request-body.ts";

const request = (body: string | ReadableStream<Uint8Array>, headers?: Record<string,string>) => new Request("http://localhost/", { method: "POST", body, headers, duplex: "half" } as RequestInit);
const rejects = async (req: Request, maximum: number, status: number) => assert.rejects(() => readObjectBounded(req, maximum), (error: unknown) => error instanceof RequestBodyError && error.status === status);

test("accepts object at the exact UTF-8 byte ceiling", async () => {
  const body = JSON.stringify({ detail: "λ" });
  assert.deepEqual(await readObjectBounded(request(body), new TextEncoder().encode(body).length), { detail: "λ" });
  await rejects(request(body), new TextEncoder().encode(body).length - 1, 413);
});
test("chunked oversized stream is canceled as soon as ceiling is crossed", async () => {
  let pulls = 0, canceled = false;
  const stream = new ReadableStream<Uint8Array>({ pull(controller) { pulls++; controller.enqueue(new Uint8Array(16)); }, cancel() { canceled = true; } }, { highWaterMark: 0 });
  await rejects(request(stream), 32, 413);
  assert.equal(canceled, true); assert.equal(pulls, 3);
});
test("rejects oversized declaration without reading body", async () => {
  let pulls = 0;
  const stream = new ReadableStream<Uint8Array>({ pull(controller) { pulls++; controller.enqueue(new Uint8Array(1)); } }, { highWaterMark: 0 });
  await rejects(request(stream, { "content-length": "33" }), 32, 413); assert.equal(pulls, 0);
});
test("rejects null, arrays, scalars, empty, malformed JSON and UTF-8", async () => {
  for (const body of ["null", "[]", "4", '"text"', "", "{"]) await rejects(request(body), 100, 400);
  const stream = new ReadableStream<Uint8Array>({ start(c) { c.enqueue(new Uint8Array([0xc0, 0xff])); c.close(); } });
  await rejects(request(stream), 100, 400);
});
test("stream errors propagate without being converted into success", async () => {
  const stream = new ReadableStream<Uint8Array>({ start(c) { c.error(new Error("interrupted")); } });
  await assert.rejects(() => readObjectBounded(request(stream), 100), /interrupted/);
});

test("fragmented UTF-8 and many empty chunks keep the same byte ceiling", async () => {
  const expected = { detail: "λ".repeat(5000) };
  const body = new TextEncoder().encode(JSON.stringify(expected));
  let pulls = 0, offset = 0;
  const stream = new ReadableStream<Uint8Array>({ pull(c) {
    pulls++;
    if (pulls <= 20000 || pulls % 2 === 0) { c.enqueue(new Uint8Array(0)); return; }
    if (offset < body.length) c.enqueue(body.subarray(offset, ++offset));
    else c.close();
  } }, { highWaterMark: 0 });
  assert.deepEqual(await readObjectBounded(request(stream), body.length), expected);
  assert.equal(offset, body.length);
  assert.ok(pulls > 20000);
});

test("a closed stream of empty chunks is invalid JSON", async () => {
  let empty = 1000;
  const stream = new ReadableStream<Uint8Array>({ pull(c) {
    if (empty-- > 0) c.enqueue(new Uint8Array(0)); else c.close();
  } }, { highWaterMark: 0 });
  await rejects(request(stream), 32, 400);
});
