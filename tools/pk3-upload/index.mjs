#!/usr/bin/env node
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { createPk3UploadService } from "./service.mjs";
import { validOrigin } from "./validation.mjs";

// Operator-only deployment configuration. Never bundle private keys into
// this repository and never run the web queue inside a match process.
const root = process.env.PK3_STORAGE_ROOT;
const converter = process.env.PK3_CONVERTER_EXEC;
const origins = (process.env.PK3_ALLOWED_ORIGINS || "")
  .split(",").map(x => x.trim()).filter(Boolean);
const port = Number(process.env.PK3_PORT || 8094);
const hostname = process.env.PK3_HOST || "127.0.0.1";
const url = process.env.PK3_SUPABASE_URL ||
  "https://hwcjaygoistufktorbmf.supabase.co";
const key = process.env.PK3_SUPABASE_PUBLISHABLE_KEY ||
  "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

if (!path.isAbsolute(root || "") || !path.isAbsolute(converter || "") ||
    origins.length < 1 || origins.some(value => !validOrigin(value)) ||
    !Number.isInteger(port) || port < 1024 || port > 65535 ||
    !["127.0.0.1", "::1"].includes(hostname) &&
      !(hostname === "0.0.0.0" && process.env.PK3_CONTAINER_BIND === "1") ||
    !/^sb_publishable_[a-zA-Z0-9_-]+$/.test(key) ||
    new URL(url).protocol !== "https:") {
  console.error("Configure PK3_STORAGE_ROOT, PK3_CONVERTER_EXEC, PK3_ALLOWED_ORIGINS, PK3_PORT and a loopback PK3_HOST.");
  process.exitCode = 2;
} else {
  const st = await fs.lstat(converter);
  if (!st.isFile() || st.isSymbolicLink())
    throw new Error("Converter executable must be a regular file.");
  // Disposable conversion artifacts must outlive individual job directories.
  // The converter enforces its own 512 MiB/eight-entry retention budget.
  const conversionCache = path.join(root, "conversion-cache");
  await fs.mkdir(conversionCache, { recursive: true, mode: 0o700 });
  const cacheInfo = await fs.lstat(conversionCache);
  if (!cacheInfo.isDirectory() || cacheInfo.isSymbolicLink())
    throw new Error("Conversion cache must be a private directory.");
  // Only keyless Supabase verification via the existing Community ticket
  // function. The service_role credential never enters this process.
  const cache = new Map();
  async function verifyTicket(ticket) {
    const cacheId = createHash("sha256").update(ticket).digest("hex");
    const known = cache.get(cacheId);
    if (known && known.expiresAt > Date.now() + 10_000) return known;
    const response = await fetch(
      new URL("/functions/v1/community-map-ticket", url), {
        method: "POST", redirect: "error",
        headers: { apikey: key, "Content-Type": "application/json" },
        body: JSON.stringify({ action: "verify", ticket }),
        signal: AbortSignal.timeout(10000),
      });
    if ([400, 401, 403].includes(response.status)) return null;
    if (!response.ok) throw new Error("Ticket verification service unavailable");
    const data = await response.json();
    if (!data || typeof data.creator_id !== "string" ||
        !/^hunter:[a-f0-9-]{36}$/i.test(data.creator_id) ||
        !Number.isFinite(Date.parse(data.expires_at)) ||
        Date.parse(data.expires_at) <= Date.now()) return null;
    const value = { creatorId: data.creator_id, expiresAt: Date.parse(data.expires_at) };
    cache.set(cacheId, value);
    if (cache.size > 512) {
      for (const [id, cached] of cache)
        if (cached.expiresAt <= Date.now()) cache.delete(id);
      if (cache.size > 512) cache.clear();
    }
    return value;
  }

  // A child process performs the expensive parsing and compilation. Pass an
  // explicit safe env, not the parent's Supabase settings. An operator must
  // additionally apply OS network/FS isolation and cgroup resource limits.
  async function runConverter({ input, output, roomName, levelName, cwd, signal }) {
    signal?.throwIfAborted();
    const args = ["--source", input, "--output", output, "--room", roomName];
    if (levelName) args.push("--level", levelName);
    return new Promise((resolve, reject) => {
      const child = spawn(converter, args, {
        cwd, shell: false, detached: process.platform !== "win32",
        stdio: ["ignore", "pipe", "pipe"],
        env: {
          PATH: "/usr/bin:/bin",
          HOME: cwd,
          XDG_DATA_HOME: cwd,
          XDG_CACHE_HOME: cwd,
          DOTNET_CLI_HOME: cwd,
          TMPDIR: cwd,
          PROJECT_PRIME_Q3_CACHE: conversionCache,
        },
      });
      let stdout = "", stderrBytes = 0, finished = false;
      const kill = () => {
        if (!child.pid) return;
        try {
          if (process.platform !== "win32") process.kill(-child.pid, "SIGKILL");
          else child.kill("SIGKILL");
        } catch { child.kill("SIGKILL"); }
      };
      const timer = setTimeout(() => {
        kill();
        if (!finished) reject(new Error("Conversion exceeded 300-second budget."));
      }, 300_000);
      timer.unref();
      signal?.addEventListener("abort", kill, { once: true });
      child.once("spawn", () => { if (signal?.aborted) kill(); });
      child.stdout.setEncoding("utf8");
      child.stdout.on("data", block => {
        stdout += block;
        if (stdout.length > 16 * 1024) kill();
      });
      child.stderr.on("data", block => {
        stderrBytes += block.length;
        if (stderrBytes > 1024 * 1024) kill();
      });
      child.on("error", error => {
        signal?.removeEventListener("abort", kill);
        clearTimeout(timer); finished = true; reject(error);
      });
      child.on("close", code => {
        signal?.removeEventListener("abort", kill);
        clearTimeout(timer); finished = true;
        if (signal?.aborted) return reject(signal.reason);
        if (code !== 0) return reject(new Error("Converter exited without verified output"));
        try {
          const result = JSON.parse(stdout.trim());
          resolve(result);
        } catch { reject(new Error("Converter output is not valid JSON")); }
      });
    });
  }

  const api = createPk3UploadService({
    root, allowedOrigins: origins, verifyTicket, runConverter,
  });
  await api.init();
  api.server.listen(port, hostname, () => {
    console.log("PK3 conversion API listening on " + hostname + ":" + port);
  });
  for (const signal of ["SIGINT", "SIGTERM"]) {
    process.on(signal, () => {
      void api.close().then(() => process.exit(0));
    });
  }
}
