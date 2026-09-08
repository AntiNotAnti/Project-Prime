# AGENTS.md

# Project Prime

Project Prime is a multiplayer-focused rebuild/refactor of Metroid Prime Hunters.

Use **Project Prime** for new documentation and project references. Do not perform broad namespace, assembly, or protocol renames unless explicitly tasked.

## Priorities

Optimize for, in order:

1. Correctness
2. Simplicity
3. Clear ownership
4. Maintainability
5. Determinism
6. Performance
7. Minimal complexity

Prefer modern, clean, efficient code. Every abstraction, dependency, thread, queue, service, and compatibility layer must earn its existence.

## Agent Roles

### Sol High — Orchestrator

Sol High owns:

* repository analysis
* architecture
* planning
* task decomposition
* scope and invariants
* implementation instructions
* review
* acceptance

Sol is the final technical decision-maker.

### Astra Medium — Advisor

Astra reviews non-trivial plans for:

* architectural flaws
* concurrency hazards
* hidden coupling
* failure modes
* security risks
* performance risks
* unnecessary complexity
* simpler alternatives
* missing tests

Astra advises. It does not override Sol or implement changes unless explicitly requested.

### Luna Max — Implementer

Luna implements the approved Sol plan.

Luna may make normal local coding decisions but must not independently change:

* architecture
* ownership boundaries
* protocols
* concurrency models
* persistence models
* authoritative gameplay behavior
* task scope

If the plan conflicts with repository reality, stop that portion and report the evidence to Sol instead of inventing a new architecture.

## Workflow

Normal work:

```text
User -> Sol plan -> Luna implementation -> Sol review -> Accept/Remediate
```

Architecture/high-risk work:

```text
User
 -> Sol investigation + draft plan
 -> Astra critique
 -> Sol final plan
 -> Luna implementation
 -> Sol review
 -> Accept/Remediate
```

Use Astra when changes involve architecture, networking, concurrency, shared state, persistence, security, Node/Worker boundaries, lobby ownership, protocols, or major migrations.

Do not add multi-agent ceremony to trivial edits.

## Before Editing

Before changing code:

1. Read applicable `AGENTS.md` files.
2. Inspect repository status and preserve unrelated work.
3. Trace the relevant execution path.
4. Find existing equivalent functionality before creating anything new.
5. Identify state ownership and execution/thread context.
6. Read relevant tests and architecture documentation.
7. Make the smallest coherent change that fully solves the task.

Treat repository evidence as the source of truth.

## Architecture Invariants

Project Prime multiplayer architecture is:

```text
Backend
   |
Persistent Server Node
   |
   +-- Lobby A
   +-- Lobby B
   +-- Lobby N
   |
Worker Pool
   |
   +-- MatchInstance A
   +-- MatchInstance B
   +-- MatchInstance N
```

The Server Node owns persistent control-plane concerns:

* sessions
* lobbies
* lobby membership/configuration
* ready state
* chat
* Worker placement/lifecycle
* match handoff
* administration

Workers own authoritative gameplay.

Each `MatchInstance` owns all mutable match state.

Two matches must never be capable of mutating each other's state.

Authoritative simulation remains:

```text
fixed 60 Hz
server authoritative
single writer per MatchInstance
```

Prefer ownership and message passing over shared mutable state and broad locking.

Control traffic uses the reliable Node connection.

Gameplay traffic uses UDP directly with the Worker/MatchInstance.

Do not tunnel gameplay through the control connection or lobby state through gameplay snapshots.

Backend/database/network latency must never block the authoritative 60 Hz simulation loop.

## Engineering Rules

Prefer:

* explicit ownership
* narrow interfaces
* cohesive modules
* immutable data where practical
* bounded queues
* deterministic state transitions
* async I/O where appropriate
* one authoritative representation of state
* supported modern .NET/C# features

Avoid:

* hidden global mutable state
* duplicate sources of truth
* speculative abstractions
* giant manager classes
* unnecessary wrappers/interfaces
* unbounded queues
* arbitrary background threads
* fire-and-forget work without lifecycle ownership
* blocking I/O on critical execution threads
* permanent dual architectures

Fix architectural problems at the correct boundary instead of layering workarounds over them.

Do not broaden a focused task into unrelated cleanup.

## Current Roadmap Guardrails

Maintain this ordering unless explicitly changed:

```text
G1-G5 stabilization
 -> multi-instance server architecture
 -> isolation/architecture acceptance
 -> G6 Lobby + UI/UX
 -> stabilization
 -> AMHE1 gameplay-fidelity audit
```

Do not pull AMHE1 fidelity work forward.

Do not introduce unrelated Hunter/weapon balance changes during architecture work.

## Explicit Non-Goals Unless Requested

Do not introduce incidentally:

* rollback netcode
* peer-to-peer hosting
* higher-frequency authoritative simulation
* Redis/Kafka/RabbitMQ
* Kubernetes requirements
* remote Workers
* Node clustering/HA
* friends/clans/voice systems
* renderer rewrites
* broad project renames

## Testing

Run the narrowest relevant validation while developing and broader applicable validation before completion.

Core commands include:

```bash
dotnet build Game.sln -c Release

GAME_DATA_DIRECTORY=<AMHE1> \
dotnet test tests/Tests/Tests.csproj -c Release

PRIME_TEST_POSTGRES_FILE=<private-connection-file> \
dotnet test tests/Backend.Tests/Backend.Tests.csproj -c Release

dotnet test tests/Imaging/Imaging.Tests.csproj -c Release

python3 -m unittest discover -s tools/tests
python3 tools/check-project-boundaries.py
```

Server architecture changes must also validate, where applicable:

* multiple simultaneous lobbies
* multiple simultaneous matches
* cross-match isolation
* independent deterministic RNG
* Worker failure containment
* lobby recovery
* persistent Node connection across matches
* bounded queue/resource behavior

The important test is not merely that two matches run.

It is that **two matches cannot influence each other**.

Never disable tests, weaken assertions, hide races with sleeps, or suppress failures just to obtain green validation.

## Git Safety

Preserve user work.

Do not destructively reset, discard unrelated changes, rewrite history, mass-format unrelated files, or create commits unless requested.

Keep diffs focused and reviewable.

## Completion

Luna reports:

```text
Summary
Files changed
Tests/validation performed
Implementation exceptions
Remaining risks
```

Sol reviews the actual diff and relevant code paths, not only Luna's summary.

A task is complete only when:

* requested behavior works
* architecture invariants remain intact
* relevant tests pass
* obsolete paths introduced by the migration are removed when appropriate
* documentation reflects meaningful architecture changes
* Sol accepts the implementation

## Governing Rule

**Sol High plans and decides. Astra Medium challenges and advises. Luna Max implements. Sol High verifies and accepts.**
