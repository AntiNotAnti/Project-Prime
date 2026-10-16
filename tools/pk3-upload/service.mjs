import http from "node:http";
import path from "node:path";
import { createHash, randomUUID } from "node:crypto";
import { createReadStream } from "node:fs";
import fs from "node:fs/promises";
import {
  CHUNK_BYTES, MAX_INPUT_BYTES, MAX_OUTPUT_BYTES,
  MAX_UPLOADS_PER_CREATOR, MAX_JOBS_PER_CREATOR,
  validHash, validJobId, validOrigin, validStart,
  canonicalState, sessionKey,
} from "./validation.mjs";

const DAY_MS = 24 * 60 * 60 * 1000;
const MAX_DISK_BYTES = 2 * 1024 * 1024 * 1024;
const MAX_JOBS = 16;
const DISK_MARGIN_BYTES = 256 * 1024 * 1024;

async function digest(file) {
  const h = createHash("sha256");
  for await (const chunk of createReadStream(file, { highWaterMark: 1024 * 1024 }))
    h.update(chunk);
  return h.digest("hex");
}
async function readJson(file) {
  try { return JSON.parse(await fs.readFile(file, "utf8")); }
  catch (error) {
    if (error?.code === "ENOENT") return null;
    throw error;
  }
}
async function atomicJson(file, value) {
  const temp = file + "." + randomUUID() + ".tmp";
  try {
    await fs.writeFile(temp, JSON.stringify(value), { mode: 0o600, flag: "wx" });
    await fs.rename(temp, file);
  } finally { await fs.rm(temp, { force: true }); }
}
async function files(root) {
  try { return await fs.readdir(root, { withFileTypes: true }); }
  catch (e) { if (e.code === "ENOENT") return []; throw e; }
}

export function createPk3UploadService({
  root, allowedOrigins, verifyTicket, runConverter,
  maxBytes = MAX_INPUT_BYTES, maxOutputBytes = MAX_OUTPUT_BYTES,
  now = () => Date.now(),
} = {}) {
  if (!path.isAbsolute(root || "") || !Array.isArray(allowedOrigins) ||
      !allowedOrigins.length || allowedOrigins.some(value => !validOrigin(value)) ||
      typeof verifyTicket !== "function" || typeof runConverter !== "function" ||
      !Number.isInteger(maxBytes) || maxBytes < 1 || maxBytes > MAX_INPUT_BYTES ||
      !Number.isInteger(maxOutputBytes) || maxOutputBytes > MAX_OUTPUT_BYTES) {
    throw new Error("PK3 upload service is not safely configured.");
  }

  const originSet = new Set(allowedOrigins);
  const uploadRoot = path.join(root, "uploads");
  const jobRoot = path.join(root, "jobs");
  const locked = new Set();
  let activeJob = null;
  let closing = false;
  let service;
  let cleanupTimer;

  function send(res, status, value, extra = {}) {
    if (res.headersSent) return;
    res.writeHead(status, {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "private, no-store",
      "X-Content-Type-Options": "nosniff",
      ...extra,
    });
    res.end(JSON.stringify(value));
  }
  function statePublic(s) {
    return {
      hash: s.hash, bytes: s.bytes, offset: s.offset,
      chunkBytes: CHUNK_BYTES, complete: !!s.jobId, jobId: s.jobId ?? null,
    };
  }
  function jobPublic(j) {
    return {
      jobId: j.id, status: j.status, stage: j.stage || j.status,
      hash: j.status === "ready" ? j.outputHash : null,
      bytes: j.status === "ready" ? j.outputBytes : null,
      mapId: j.status === "ready" ? j.mapId : null,
      roomName: j.roomName, levelName: j.levelName ?? null,
      warnings: j.status === "ready" ? j.warnings || [] : [],
      reason: j.status === "failed" ? "Conversion failed. Review map format or retry in Studio." : null,
    };
  }
  async function getSession(owner, hash) {
    const key = sessionKey(owner, hash);
    const dir = path.join(uploadRoot, key);
    const meta = await readJson(path.join(dir, "session.json"));
    if (!canonicalState(meta) || meta.creatorId !== owner || meta.hash !== hash)
      return null;
    return { ...meta, dir, key };
  }
  async function getJob(owner, id) {
    if (!validJobId(id)) return null;
    const dir = path.join(jobRoot, id);
    const job = await readJson(path.join(dir, "job.json"));
    if (!job || job.id !== id || job.creatorId !== owner ||
        !validHash(job.sourceHash)) return null;
    return { ...job, dir };
  }

  async function startJobQueue() {
    if (activeJob || closing) return;
    const candidates = [];
    for (const entry of await files(jobRoot)) {
      if (!entry.isDirectory() || !validJobId(entry.name)) continue;
      const j = await readJson(path.join(jobRoot, entry.name, "job.json"));
      if (j && j.id === entry.name && j.status === "queued")
        candidates.push(j);
    }
    candidates.sort((a, b) => a.createdAt - b.createdAt);
    const job = candidates[0];
    if (!job || activeJob || closing) return;
    activeJob = job.id;
    const folder = path.join(jobRoot, job.id);
    const input = path.join(folder, "input.pk3");
    const output = path.join(folder, "output.ppmap");
    const saved = { ...job, status: "running", stage: "Converting BSP and textures" };
    try {
      await atomicJson(path.join(folder, "job.json"), saved);
      const result = await runConverter({
        input, output, roomName: job.roomName,
        levelName: job.levelName || null, cwd: folder,
      });
      const size = (await fs.stat(output)).size;
      if (size < 1 || size > maxOutputBytes || !validHash(result.packageHash) ||
          !/^[a-f0-9-]{36}$/i.test(result.mapId) ||
          !validHash(result.contentHash) || result.packageBytes !== size ||
          await digest(output) !== result.packageHash) {
        throw new Error("Worker output did not match its validated package report.");
      }
      await atomicJson(path.join(folder, "job.json"), {
        ...saved, status: "ready", stage: "Portable .ppmap verified",
        outputHash: result.packageHash, outputBytes: size, mapId: result.mapId,
        warnings: Array.isArray(result.warnings)
          ? result.warnings.filter(x => typeof x === "string")
              .slice(0, 32).map(x => x.slice(0, 250)) : [],
        finishedAt: now(),
      });
    } catch (error) {
      // Preserve no worker-internal path/stack traces in public job state.
      console.error("PK3 conversion job failed", job.id, error?.name || "unknown");
      await fs.rm(output, { force: true });
      await atomicJson(path.join(folder, "job.json"), {
        ...saved, status: "failed", stage: "Conversion failed", finishedAt: now(),
      });
    } finally {
      activeJob = null;
      if (!closing) setImmediate(() => void startJobQueue().catch(console.error));
    }
  }

  async function capacity(creatorId, requestedBytes) {
    let pending = 0, count = 0;
    for (const item of await files(uploadRoot)) {
      if (!item.isDirectory()) continue;
      const s = await readJson(path.join(uploadRoot, item.name, "session.json"));
      if (s && !s.jobId && now() - s.updatedAt < DAY_MS) {
        pending += s.bytes; if (s.creatorId === creatorId) count++;
      }
    }
    let jobs = 0, perCreator = 0, used = pending;
    for (const item of await files(jobRoot)) {
      if (!item.isDirectory()) continue;
      const j = await readJson(path.join(jobRoot, item.name, "job.json"));
      if (!j || now() - j.createdAt >= DAY_MS) continue;
      jobs++; if (j.creatorId === creatorId) perCreator++;
      used += Number(j.bytes || 0) + Number(j.outputBytes || 0);
    }
    if (count >= MAX_UPLOADS_PER_CREATOR || perCreator >= MAX_JOBS_PER_CREATOR ||
        jobs >= MAX_JOBS || used + requestedBytes > MAX_DISK_BYTES)
      return false;
    if (typeof fs.statfs === "function") {
      const stat = await fs.statfs(root);
      if (Number(stat.bavail) * Number(stat.bsize) <
          requestedBytes + DISK_MARGIN_BYTES) return false;
    }
    return true;
  }
  async function cleanup() {
    const cutoff = now() - DAY_MS;
    for (const [base, manifest] of [[uploadRoot, "session.json"], [jobRoot, "job.json"]]) {
      for (const item of await files(base)) {
        if (!item.isDirectory() || locked.has(item.name) ||
            item.name === activeJob) continue;
        const dir = path.join(base, item.name);
        const info = await readJson(path.join(dir, manifest));
        if (!info || Number(info.updatedAt || info.finishedAt || info.createdAt) < cutoff)
          await fs.rm(dir, { recursive: true, force: true });
      }
    }
  }
  async function init() {
    await fs.mkdir(root, { recursive: true, mode: 0o700 });
    const info = await fs.lstat(root);
    if (!info.isDirectory() || info.isSymbolicLink())
      throw new Error("PK3 service data root must be a private directory.");
    await fs.mkdir(uploadRoot, { recursive: true, mode: 0o700 });
    await fs.mkdir(jobRoot, { recursive: true, mode: 0o700 });
    // Jobs interrupted by a service restart may be replayed from the same
    // immutable hashed source, without accepting duplicate source uploads.
    for (const item of await files(jobRoot)) {
      if (!item.isDirectory() || !validJobId(item.name)) continue;
      const p = path.join(jobRoot, item.name, "job.json");
      const job = await readJson(p);
      if (job?.status === "running") {
        await fs.rm(path.join(jobRoot, item.name, "output.ppmap"), { force: true });
        await atomicJson(p, { ...job, status: "queued", stage: "Restarted after interruption" });
      }
    }
    await cleanup();
    cleanupTimer = setInterval(() => void cleanup().catch(console.error), 30 * 60 * 1000);
    cleanupTimer.unref();
    setImmediate(() => void startJobQueue().catch(console.error));
  }

  service = http.createServer(async (req, res) => {
    const origin = req.headers.origin;
    if (origin) {
      if (!originSet.has(origin)) return send(res, 403, { error: "origin_denied" });
      res.setHeader("Access-Control-Allow-Origin", origin);
      res.setHeader("Vary", "Origin");
      res.setHeader("Access-Control-Expose-Headers",
        "X-Prime-SHA256, Content-Disposition, Retry-After");
    }
    if (req.method === "OPTIONS") {
      if (!origin || !originSet.has(origin))
        return send(res, 403, { error: "origin_denied" });
      res.writeHead(204, {
        "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
        "Access-Control-Allow-Headers": "Authorization, Content-Type",
        "Access-Control-Max-Age": "600",
      });
      return res.end();
    }
    if (req.method === "GET" && req.url === "/health")
      return send(res, 200, { ok: true });

    let identity;
    try {
      if (typeof req.headers.authorization !== "string" ||
          !/^Bearer ppm1\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(req.headers.authorization) ||
          req.headers.authorization.length > 3100) {
        return send(res, 401, { error: "hunter_license_required" });
      }
      identity = await verifyTicket(req.headers.authorization.slice(7));
      if (!identity || !/^hunter:[a-f0-9-]{36}$/i.test(identity.creatorId))
        return send(res, 401, { error: "hunter_license_invalid" });
    } catch {
      return send(res, 503, { error: "identity_service_unavailable" });
    }

    try {
      const url = new URL(req.url || "/", "http://localhost");
      const owner = identity.creatorId;
      const source = /^\/v1\/sources\/([0-9a-f]{64})(?:\/(complete))?$/.exec(url.pathname);
      const job = /^\/v1\/conversions\/([0-9a-f-]{36})(?:\/(package))?$/.exec(url.pathname);
      if (source) {
        const hash = source[1], operation = source[2];
        const key = sessionKey(owner, hash);
        const dir = path.join(uploadRoot, key);
        if (req.method === "GET" && !operation) {
          const s = await getSession(owner, hash);
          return s ? send(res, 200, statePublic(s)) :
            send(res, 404, { error: "source_not_found" });
        }
        if (locked.has(key)) return send(res, 409, { error: "upload_busy" });
        locked.add(key);
        try {
          if (req.method === "POST" && !operation) {
            let body = "";
            for await (const block of req) {
              body += block.toString("utf8");
              if (body.length > 4096) return send(res, 413, { error: "metadata_too_large" });
            }
            let parsed;
            try { parsed = JSON.parse(body); }
            catch { return send(res, 400, { error: "invalid_json" }); }
            if (!validStart(parsed, maxBytes))
              return send(res, 400, { error: "invalid_source_metadata" });
            let state = await getSession(owner, hash);
            if (state) {
              if (state.bytes !== parsed.bytes || state.roomName !== parsed.roomName ||
                  state.levelName !== (parsed.levelName ?? null))
                return send(res, 409, { error: "source_metadata_changed" });
              return send(res, 200, statePublic(state));
            }
            if (!await capacity(owner, parsed.bytes))
              return send(res, 507, { error: "creator_quota_or_storage_exceeded" });
            await fs.mkdir(dir, { recursive: true, mode: 0o700 });
            const meta = {
              hash, creatorId: owner, bytes: parsed.bytes, offset: 0,
              roomName: parsed.roomName, levelName: parsed.levelName ?? null,
              createdAt: now(), updatedAt: now(), jobId: null,
            };
            const handle = await fs.open(path.join(dir, "source.part"), "wx", 0o600);
            await handle.close();
            await atomicJson(path.join(dir, "session.json"), meta);
            return send(res, 201, statePublic(meta));
          }
          const state = await getSession(owner, hash);
          if (!state) return send(res, 404, { error: "source_not_found" });
          if (req.method === "PUT" && !operation) {
            if (state.jobId) return send(res, 409, { error: "source_already_converted" });
            const offset = url.searchParams.get("offset");
            const length = Number(req.headers["content-length"]);
            if (!/^(0|[1-9]\d*)$/.test(offset || "") ||
                Number(offset) !== state.offset ||
                !Number.isSafeInteger(length) || length < 1 ||
                length > CHUNK_BYTES || length > state.bytes - state.offset)
              return send(res, 409, { error: "invalid_upload_range", offset: state.offset });
            const data = await fs.open(path.join(dir, "source.part"), "r+");
            let written = 0;
            try {
              for await (const chunk of req) {
                written += chunk.length;
                if (written > length) throw new Error("Body exceeds declared upload length.");
                let position = 0;
                while (position < chunk.length) {
                  const part = await data.write(chunk, position, chunk.length - position,
                    state.offset + written - chunk.length + position);
                  if (part.bytesWritten <= 0) throw new Error("Upload short write.");
                  position += part.bytesWritten;
                }
              }
              if (written !== length) throw new Error("Upload incomplete.");
              await data.sync();
              state.offset += written;
              state.updatedAt = now();
              await atomicJson(path.join(dir, "session.json"), state);
            } catch {
              await data.truncate(state.offset);
              return send(res, 400, { error: "upload_body_incomplete" });
            } finally { await data.close(); }
            return send(res, 200, statePublic(state));
          }
          if (req.method === "POST" && operation === "complete") {
            if (state.jobId) return send(res, 202, { jobId: state.jobId });
            if (state.offset !== state.bytes) return send(res, 409,
              { error: "upload_incomplete", offset: state.offset });
            if (await digest(path.join(dir, "source.part")) !== hash)
              return send(res, 422, { error: "source_sha256_mismatch" });
            if (!await capacity(owner, 0))
              return send(res, 507, { error: "conversion_queue_full" });
            const id = randomUUID();
            const folder = path.join(jobRoot, id);
            await fs.mkdir(folder, { mode: 0o700 });
            await fs.rename(path.join(dir, "source.part"), path.join(folder, "input.pk3"));
            const jobMeta = {
              id, creatorId: owner, sourceHash: hash, bytes: state.bytes,
              roomName: state.roomName, levelName: state.levelName,
              createdAt: now(), updatedAt: now(), status: "queued", stage: "Queued",
            };
            await atomicJson(path.join(folder, "job.json"), jobMeta);
            state.jobId = id;
            state.updatedAt = now();
            await atomicJson(path.join(dir, "session.json"), state);
            setImmediate(() => void startJobQueue().catch(console.error));
            return send(res, 202, { jobId: id });
          }
        } finally { locked.delete(key); }
      }
      if (job && req.method === "GET") {
        const data = await getJob(owner, job[1]);
        if (!data) return send(res, 404, { error: "conversion_not_found" });
        if (!job[2]) return send(res, 200, jobPublic(data));
        if (data.status !== "ready" || !validHash(data.outputHash))
          return send(res, 409, { error: "conversion_not_ready" });
        const file = path.join(data.dir, "output.ppmap");
        const length = (await fs.stat(file)).size;
        if (length !== data.outputBytes || length < 1 || length > maxOutputBytes)
          return send(res, 409, { error: "conversion_output_invalid" });
        res.writeHead(200, {
          "Content-Type": "application/octet-stream",
          "Cache-Control": "private, no-store",
          "Content-Length": String(length),
          "Content-Disposition": 'attachment; filename="' + data.outputHash + '.ppmap"',
          "X-Prime-SHA256": data.outputHash,
          "X-Content-Type-Options": "nosniff",
        });
        const stream = createReadStream(file, { highWaterMark: 256 * 1024 });
        stream.on("error", () => res.destroy());
        return stream.pipe(res);
      }
      return send(res, 404, { error: "route_not_found" });
    } catch (error) {
      console.error("PK3 API request error", error?.name || "unknown");
      if (!res.headersSent) send(res, 500, { error: "pk3_service_error" });
      else res.destroy();
    }
  });

  return {
    server: service,
    init,
    async close() {
      closing = true;
      clearInterval(cleanupTimer);
      await new Promise(resolve => service.close(() => resolve()));
    },
  };
}
