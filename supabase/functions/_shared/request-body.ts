export class RequestBodyError extends Error {
  readonly status: 400 | 413;
  constructor(status: 400 | 413, message: string) { super(message); this.status = status; }
}

/** Apply the byte ceiling during stream consumption, before JSON/string allocation. */
export async function readObjectBounded(req: Request, maximum: number): Promise<Record<string, unknown>> {
  const length = req.headers.get("content-length");
  if (length !== null && (!/^\d+$/.test(length) || Number(length) > maximum)) {
    throw new RequestBodyError(413, "payload_too_large");
  }
  // Keep storage bounded by bytes rather than by the number of stream chunks.
  // Empty or one-byte chunks must not grow an unbounded metadata array.
  let bytes = new Uint8Array(Math.min(maximum, 4096));
  let count = 0;
  const reader = req.body?.getReader();
  if (reader) {
    try {
      while (true) {
        const next = await reader.read();
        if (next.done) break;
        if (next.value.length === 0) continue;
        const required = count + next.value.length;
        if (required > maximum) {
          await reader.cancel().catch(() => {});
          throw new RequestBodyError(413, "payload_too_large");
        }
        if (required > bytes.length) {
          const grown = new Uint8Array(Math.min(maximum, Math.max(required, bytes.length * 2)));
          grown.set(bytes.subarray(0, count));
          bytes = grown;
        }
        bytes.set(next.value, count);
        count = required;
      }
    } finally { reader.releaseLock(); }
  }
  try {
    const value: unknown = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes.subarray(0, count)));
    if (value === null || typeof value !== "object" || Array.isArray(value)) throw new Error("object required");
    return value as Record<string, unknown>;
  } catch { throw new RequestBodyError(400, "invalid_json_object"); }
}
