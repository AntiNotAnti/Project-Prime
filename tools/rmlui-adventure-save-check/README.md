# Native Adventure save management checks

Run `dotnet run --project tools/rmlui-adventure-save-check -c Release`.

This source-linked fixture passed 33 checks. It runs the actual guarded Adventure byte owner inside a new temporary working tree, with unrelated engine symbols stubbed only so no assets or serializer are initialized. It also tests the actual portable confirmation draft. Original files, all backups, failed publication and staging cleanup are checked byte-for-byte.

It does not run the application owner admission or native page; those have separate composed managed/native fixtures. No user save, active game state or serializer is touched.
