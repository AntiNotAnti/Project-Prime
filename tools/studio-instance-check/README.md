# Studio instance protocol CPU check

Run `dotnet run --project tools/studio-instance-check -c Release` with .NET 10.
The harness compiles only the BCL Studio integration sources. It creates isolated
temporary IPC directories and never starts a game, Studio executable, or GPU.

Startup integration:

- Call `StudioApplicationInstance.AcquireStudioInstance(args)` before opening the
  Studio window. Supported arguments are `--studio`, `--replay ABSOLUTE.ppdemo`,
  `--clip ABSOLUTE.ppclip`, `--replay-library`, `--recover`, and `--safe-mode`. Maps are rejected with
  the coming-soon message. A typed `StudioOpenRequest` overload is also available.
- If `Forwarded`, exit the secondary process successfully. If `Error` is non-null,
  surface it and exit. Otherwise retain the primary instance until shutdown.
- On the same thread that acquired it, call `Pump(request => ...)` each native UI
  tick. The initial primary request is queued too. A forwarding acknowledgment
  means accepted into the bounded queue; document loading remains the UI owner's
  responsibility. The callback must handle document-open errors itself.
- Dispose on shutdown. The authenticated receiver stops before its descriptor and
  durable instance lock are retired. The lock file itself is never unlinked.

`StudioApplicationLauncher.TryOpen(documentPath, recover, out error)` discovers the
paired Studio executable and uses separate process arguments. It preserves normal
inherited environment and supplies a one-shot random IPC launch capability.
The receiver consumes and removes that environment value immediately.

IPC is keyed by canonical installation and user-data root. Sibling macOS app
bundles share an installation identity. Explicit `userDataDirectory` takes
precedence; otherwise `PROJECT_PRIME_STUDIO_IPC_DATA`, then
`PROJECT_PRIME_USER_DATA/studio`, then the OS application-data default is used.
All Studio starts for one installation must use the same IPC data root.

`ClientInstanceGuard.TryAcquireForProcess(timeout, studio: true)` has an independent
role mutex if the shared process startup guard is also used. Do not acquire the
main game's role for a Studio process before deciding whether to forward.

Protocol transport is adapted from donor 31c3b20. It retains mutual HMAC challenge
proofs, current-user named pipes, private descriptors, bounded frames/connections,
request ID deduplication, startup retry and cancellation. Only document-open
messages are enabled; no game/account broker commands are included.

`TryOpenWorkspace(replay: false, out error)` opens the Home/map placeholder;
`TryOpenWorkspace(replay: true, out error)` opens Replay Library. These use typed
requests and the library CLI flag, rather than inventing document paths.
`CreateStartInfoWorkspace` and `CreateStartInfoRequest` expose the corresponding
argument construction for packaging/tests without starting a process.
