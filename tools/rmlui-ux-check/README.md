# Native UI review

Run with the actual client assembly, native bridge, and copied RmlUi assets:

```sh
dotnet run --project tools/rmlui-ux-check -p:PrimeAssemblyDirectory=/absolute/path/to/client/output -- /absolute/path/to/native/library /absolute/path/to/rmlui /tmp/prime-ux-previews
```

The fixture isolates user data and disables production account operations. It exercises native page layout, input actions, the packaged splash image, and a synthetic cached map thumbnail at three viewport/density combinations. PNGs rasterize the native draw list using Skia for local review. They omit engine scenery/hunters, ignore stencil clip masks, and do not establish GPU, device, or gameplay parity. Keep generated images outside the repository.
