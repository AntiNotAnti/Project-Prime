# Android updater and custom map parity audit

Project Prime has a managed .NET HttpClient and Android's native CA handling; copying a C++ libcurl certificate helper is not applicable. The update check already distinguishes 404 no release, 403/429 rate limiting, and a request failure in LastReason. This change improves failure categories for managed TLS handshake/certificate problems, DNS/socket errors, timeouts and unsupported HTTP handlers, without displaying raw exception messages or URLs.

The existing `tools/updatecheck` suite now tests each category. No updater transport, user settings, APK installation path or protocol format is changed.

Android's map installation is performed in `MainApplication` before the room table is read. `MapBuildManifest.IsCurrent` validates required outputs, and `MapVoteReadiness` guards dedicated-server map votes. No duplicate rescan is added without a failing process-restart test.

**Not proven here:** on-device release API TLS trust, Android installer activity recreation, GLES p99/overdraw, high-refresh preview, background map rescan and real APK updates. These remain real-device gates and must be reported NOT_TESTED rather than PASS.
