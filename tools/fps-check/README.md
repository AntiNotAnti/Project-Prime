# FPS conversion checks

Run `dotnet run --project tools/fps-check -c Release` from the checkout. This dependency-free .NET 10 executable links the production audit/math/trace sources and checks analytical composition, invalid domains, scanner output, missing/corrupt traces, boundary matching and evidence gates.

Run `dotnet run --project tools/fps-check -c Release -- -fpsconvertaudit [checkout] [output]` for the source inventory without game dependencies.

The complete command guide, limitations, validation results and remaining phase gates are in [the FPS audit status](../../docs/physics/FPS-CONVERSION-AUDIT.md). Passing this suite does not certify native gameplay parity.
