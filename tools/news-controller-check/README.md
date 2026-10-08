# Shared News controller and actual native page

```sh
dotnet run --project tools/news-controller-check -c Release
dotnet run --project tools/news-controller-check -c Release -p:MphReadRmlUi=true -- \
  --native artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  src/MphRead/bin/Release/net10.0/rmlui
```

The legacy NewsWorkspace and native page use the same toolkit-neutral
BundledNewsProvider. The source check preserves the exact four dispatches, all
four filters, selection/detail/close, genuine empty state, owner thread and the
approved Discord URI with recoverable browser failure. No news is invented and
no network request occurs while reading the feed.

The native check opens the actual shared-shell page at four viewport/density
combinations, uses actual DOM typed intents for every filter and dispatch,
checks readable independently flowing headline/body/cards, modal focus/lifetime,
both browser results and canonical Back. Its browser adapter is fake, so tests
never open an external browser. Actual OS browser/picker/device acceptance and
full golden parity remain separate requirements.

Native mode uses the real game's News controller types, so it does not link a
second copy of Core into the check assembly. To check an isolated existing full
client without rebuilding it, add
`-p:PrimeAssemblyDirectory=<full-client-directory>` and pass that client's
complete current asset root. The real native check reports 348 assertions.
