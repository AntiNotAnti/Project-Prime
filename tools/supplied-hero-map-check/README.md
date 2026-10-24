# Supplied CTZ and Payload packages

Checks the two shipped authored bundles through the real strict package reader,
mode validator, compiler and native runtime packer. An empty private game-data
root ensures compilation cannot borrow cartridge files. Input bundles remain
unchanged. No model importer, authoring folder, graphics context or account is needed.

Build the client first, then build/run this tool with matching configuration,
runtime identifier and output conventions. For the current Windows build:

```powershell
dotnet build tools/supplied-hero-map-check/supplied-hero-map-check.csproj -c Release -r win-x64 -p:BuildProjectReferences=false --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet tools/supplied-hero-map-check/bin/Release/net10.0/win-x64/supplied-hero-map-check.dll --maps C:/absolute/checkout/maps --output C:/absolute/new-empty-evidence-directory
```

`report.json` records assertion results, tested client identity, package identities
and generated-output hashes. Failures return a nonzero exit and preserve evidence.
This proves reading, gameplay schema and native packing compatibility; actual
CTZ/Payload startup, bot behavior and rendering remain separate native checks.
