// Strict input validation for the read-only public ranking service.
// Dependency-free so node --experimental-strip-types can run contract tests.
export const metrics = ["wins", "kills", "matches", "kd", "win_rate", "headshots"] as const;
export type LeaderboardMetric = typeof metrics[number];

export const modeNames: Record<number, string> = {
  3: "Battle", 4: "Battle Teams", 5: "Survival",
  6: "Survival Teams", 7: "Capture", 8: "Bounty",
  9: "Bounty Teams", 10: "Nodes", 11: "Nodes Teams",
  12: "Defender", 13: "Defender Teams", 14: "Prime Hunter",
  15: "Insta-Gib", 16: "Relic", 17: "Hardpoint",
  18: "Hardpoint Teams", 19: "Gun Game",
  20: "Kill Confirmed", 21: "Kill Confirmed Teams",
  22: "Headhunter", 23: "One in the Chamber",
};

export class InvalidLeaderboardQuery extends Error {}

export function parseLeaderboardRequest(url: URL) {
  const q = url.searchParams;
  const metric = q.get("metric") || "wins";
  if (!metrics.some((item) => item === metric))
    throw new InvalidLeaderboardQuery("invalid_metric");
  const mode = q.get("mode") || "all";
  if (mode !== "all" && (!/^\d{1,2}$/.test(mode) || !modeNames[Number(mode)]))
    throw new InvalidLeaderboardQuery("invalid_mode");
  const onlyPublic = q.get("only_public") || "0";
  if (onlyPublic !== "0" && onlyPublic !== "1")
    throw new InvalidLeaderboardQuery("invalid_visibility_filter");
  const rawPage = q.get("page") || "1";
  if (!/^\d{1,3}$/.test(rawPage))
    throw new InvalidLeaderboardQuery("invalid_page");
  const page = Number(rawPage);
  // All registered public Licenses may exceed the original 2,500-player
  // career-only pagination cap. Extend only the public all-mode directory;
  // normal competitive rankings retain their narrow 100-page bound.
  const maxPage = onlyPublic === "1" && mode === "all" ? 500 : 100;
  if (page < 1 || page > maxPage)
    throw new InvalidLeaderboardQuery("invalid_page");
  const rawSearch = (q.get("q") || "").normalize("NFC");
  // Validate before trimming so hidden control characters cannot disappear.
  if (Array.from(rawSearch).length > 40 ||
      Array.from(rawSearch).some((c) => c.charCodeAt(0) < 32))
    throw new InvalidLeaderboardQuery("invalid_search");
  const search = rawSearch.trim();
  const hunter = q.get("hunter");
  if (hunter !== null && !/^[a-f0-9]{32}$/.test(hunter))
    throw new InvalidLeaderboardQuery("invalid_public_hunter_id");
  return {
    metric: metric as LeaderboardMetric,
    mode, page,
    pageSize: 25,
    onlyPublic: onlyPublic === "1",
    search,
    hunter,
  };
}
export function publicIdValid(value: unknown): value is string {
  return typeof value === "string" && /^[a-f0-9]{32}$/.test(value);
}
