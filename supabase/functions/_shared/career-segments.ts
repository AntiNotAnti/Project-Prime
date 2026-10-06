// Only HMAC-verified player_id values enter grouping. Client IDs and subject hints
// are transport/accounting aids, never account authorization.
export function mergeCareerSegments(participants: any[], version: number) {
  const duplicates = new Set<string>();
  const groups = new Map<string, any[]>();
  for (const p of participants) {
    if (!p.player_id) continue;
    const entries = groups.get(p.player_id) ?? [];
    entries.push(p);
    groups.set(p.player_id, entries);
  }
  for (const [player, entries] of groups) {
    if (entries.length < 2) continue;
    entries.sort((a, b) => a.joined_ticks - b.joined_ticks || a.left_ticks - b.left_ticks
      || a.participant_id.localeCompare(b.participant_id));
    if (version === 1 || entries.some((p, i) => i > 0 && entries[i - 1].left_ticks > p.joined_ticks)) {
      // Simultaneous use of one license remains Practice; never merge overlapping
      // controls into a second authenticated result for the same account.
      duplicates.add(player);
    }
  }
  if (version === 1) {
    // Preserve the old normalized object shape, property order and UUID case:
    // already accepted immutable outbox retries must hash byte-for-byte alike.
    let missingStartedIdentity = false;
    const output = participants.map((p) => {
      const player = duplicates.has(p.player_id) ? { ...p, player_id: null } : p;
      if (player.started_match && !player.player_id) missingStartedIdentity = true;
      return player;
    });
    return { participants: output, missingStartedIdentity };
  }
  const output: any[] = [];
  const emitted = new Set<string>();
  let missingStartedIdentity = false;
  for (const p of participants) {
    if (!p.player_id || duplicates.has(p.player_id)) {
      if (p.started_match) missingStartedIdentity = true;
      output.push({ ...p, player_id: null });
      continue;
    }
    if (emitted.has(p.player_id)) continue;
    emitted.add(p.player_id);
    const entries = groups.get(p.player_id)!;
    const first = entries[0], last = entries[entries.length - 1];
    const metrics = { ...first.metrics, beam_kills: [...first.metrics.beam_kills] };
    for (const entry of entries.slice(1)) {
      for (const key of Object.keys(metrics)) {
        if (key === "beam_kills") metrics.beam_kills = metrics.beam_kills.map((n: number, i: number) => n + entry.metrics.beam_kills[i]);
        else if (key === "longest_kill_streak") metrics[key] = Math.max(metrics[key], entry.metrics[key]);
        else metrics[key] += entry.metrics[key];
      }
    }
    const started = entries.some((entry) => entry.started_match);
    output.push({
      ...last,
      participant_id: first.participant_id,
      started_match: started,
      single_hunter: entries.every((entry) => entry.single_hunter && entry.hunter === first.hunter),
      played_ticks: entries.reduce((n, entry) => n + entry.played_ticks, 0),
      joined_ticks: first.joined_ticks,
      left_ticks: last.left_ticks,
      metrics,
    });
  }
  return { participants: output, missingStartedIdentity };
}
