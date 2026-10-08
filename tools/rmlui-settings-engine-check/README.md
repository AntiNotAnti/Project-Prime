Run each mode in a separate process. The checks create and remove isolated temporary preference folders and exercise the existing authoritative settings services; they do not run inside an active client.

```sh
dotnet run --project tools/rmlui-settings-engine-check -c Release
dotnet run --project tools/rmlui-settings-engine-check -c Release -- --native
```

The first mode checks complete field capture, exact input binding fields, controller and touch saves, graphics/controller presets, HUD override inheritance, detached editor handoff, forward preferences, swallowed write detection, disk/runtime rollback, and post-save maintenance warnings.

The native mode uses the packaged RmlUi library and authored Settings documents with the draw-list backend. It checks actual DOM intents, fields, capture, video rollback, focus, and dirty navigation. A usable GPU present is deliberately required from the engine before video Keep; the separate compositor checks cover actual GPU presentation.

After building the feature-enabled client, use `-p:BuildProjectReferences=false` for the tool build to reuse that coordinated binary.
