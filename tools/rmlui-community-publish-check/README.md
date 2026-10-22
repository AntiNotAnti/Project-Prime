# Native Community publication controller check

Run `/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-community-publish-check -c Release`.

Links the portable controller directly. The 44 synthetic assertions cover frozen parent/source/visibility/notes confirmation, once-only queue admission, malformed drafts/receipts, progress and verified-success separation, pause/resume, owner thread, failed outcomes, cancellation, account retirement and serialization redaction. No disk, user accounts, packages or HTTP are used.

The actual package/queue/account adapter and native pointer/layout acceptance are separate fixtures; a portable pass does not establish live publishing.
