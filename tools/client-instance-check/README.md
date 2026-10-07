Run `dotnet run --project tools/client-instance-check -c Release`.

This BCL-only tool links the real production ClientInstanceGuard and DesktopInstallationIdentity. Eleven assertions use independent processes and OS named mutexes: separate installations coexist; same installation and obsolete mapstudio role cannot overlap; real symbolic-link launches and uncanonicalized host-base aliases cannot bypass ownership; normal release and process death permit reacquisition while unrelated installations survive. All child installations are private temporary copies.

The macOS .NET host resolves symlinks before reporting AppContext.BaseDirectory. A controlled child uses the BCL APP_CONTEXT_BASE_DIRECTORY host configuration seam before invoking the unchanged production guard API, exposing distinct actual alias paths to the same installation. The raw GetFullPath/case-fold implementation fails this overlap assertion; the shared physical resolver passes. Windows without symbolic-link privileges reports the alias portion skipped explicitly.

Early mapstudio forwarding is covered separately by game route review and startup checks before any client guard, game broker, shell or UI acquisition.
