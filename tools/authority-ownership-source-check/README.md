# Exact authority ownership helper checks

This isolated project links the actual `NetClientJoinOwnership.cs` source with a minimal lifecycle facade. It checks existing public client semantics, explicitly captured server authority, null-transport incarnation replacement, reentrant replacement, and 100 concurrent lifecycle replacements. It opens no sockets, creates no world, and builds no application project. The actual asset-backed companion is `tools/server-sim-retirement-check`.
