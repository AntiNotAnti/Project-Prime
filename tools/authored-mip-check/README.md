# Authored RGBA mip checks

Run `dotnet run --project tools/authored-mip-check -c Release` on Linux or Windows with the Ktx2.NET native runtime. On macOS, first build the repository's pinned runtime with `tools/ktx/build-native.sh osx-arm64` (or `osx-x64`); the tool copies the matching built library to its output. An existing runtime can alternatively be selected with `DYLD_LIBRARY_PATH`.

The tool links the production KTX2 decoder and prepared asset types. Its valid, synthetic KTX2 images contain deliberately distinct authored albedo, normalized-normal, material and emissive levels. It checks all four character asset classes, odd/tall/one-wide/one-texel shapes, three dimension caps, byte identity, authored suffix selection, complete versus partial sampling, malformed level layouts and exact admitted byte estimates. The small encoder only creates the fixtures; production decoding performs the assertions. No game data, HD pack, window or GPU is required.

The production desktop suites `-renderwindowcheck -renderer metal` (or another modern backend) and `-textureupdatecheck -renderer opengl` additionally upload these fixtures through TextureAssetManager. Exact per-level GPU readback verifies explicit mip uploads on each API; the checks cover base-only and partial sampling, modern upload accounting, native texture capacity, repeated cache admission/release and authored-normal restoration after device recovery. The window suite also runs the same bytes through its fresh OpenGL fallback context.

The macOS native tests establish Metal/OpenGL parity on the tested host. Windows DX12/Vulkan, Linux Vulkan and physical Android Vulkan/GLES require their own native runs; a successful compile does not establish driver or device correctness.
