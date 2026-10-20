# FPS-07: sustained sampling and native acceptance

The existing harness capped timing at 120 seconds and grew its raw sample list
inside the measured loop. Its allocation/collection results were read after
percentile calculations and scene cleanup, conflating measurement with analysis.
These instrumentation defects must be corrected before the sustained matrix.

Ordinary timing now permits 8–1804 seconds (30 minutes plus 240 warmup ticks).
Screenshot/pose constraints are unchanged. Bounded interval storage is allocated
before warmup, accepts up to 4096 returned frames per requested measured second,
and fails explicitly on overflow. Duration, allocated bytes and GC counts freeze
at the final interval before receipt generation, sorting, export and cleanup.
The offline analyzer rejects failed/overflowed/incomplete harness evidence and
labels callback-return timing separately from the profiler's swap-return timing.

This changes diagnostic sampling only. It does not change game simulation,
rendering, presentation pacing, input, replay identity or feature defaults.
Production-helper tests cover duration/capacity boundaries, zero-allocation
recording, exact capacity, overflow accounting and chronological preservation;
full harness compilation and analyzer validation pass. Windows native results
will be recorded under the exact frozen binary and source identity. Hardware
qualification is not implied by these tests or by a successful build.

Rollback: revert this slice; use the previous short-run tool with its documented
allocation-counter limitation. No player settings or game data are modified.
