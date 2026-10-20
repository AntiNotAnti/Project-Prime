# CW-05i — Android UI-owned offline rule saves

Experimental Android RmlUi uses the existing Home/settings owner for Rules
persistence. EGL dispatches one immutable mode/expected/next change; it never
writes settings or waits synchronously for the UI. Default desktop services
retain their synchronous path.

The menu admits one pending Rules save. Editing, repeated Apply and launch are
disabled while Cancel remains available. Results carry the exact change and
copied current rules. Only a current controller epoch, exact echo, successful
result, matching current values and matching service cache may report success.
Cancellation, route/lobby retirement and disposal retire the old epoch; a late
result cannot clear or overwrite a newer job.

Android captures the Home instance when constructing the launcher options.
The posted callback rechecks that exact Home, view, launcher lifetime, activity
activation, foreground, admission and absence of game/network/replay/pending
Adventure save. The Home owner validates values, holds the thread-affine
SettingsPersistence lease, compares the exact rule subset and persists its
current DTO. Unrelated settings survive; exceptions restore the rule subset.
Import/reset fences cannot be mistaken for a successful write.

Copied rules cross UI/EGL through an immutable reference cache. Owner readback
publishes independently of task completion: cancellation after commit cannot
restore stale values. If a post-write operation throws, a guarded UI-owner read
refreshes the cache but reports an unverified outcome. If the settings owner
cannot be inspected, the next render retires the experimental launcher and
returns to the existing launcher. Recreated options capture fresh Home values.

Dispatch checks cancellation before writing, executes a repeated callback at
most once, and has a10-second deadline if an Activity drops the posted callback.
Cancellation does not undo a completed write. Neither callback/cache publication
owns native RmlUi state, game simulation or network state.

The experiment remains default-off. Compile/test evidence and unrun physical
device gates are recorded in [CW-05i evidence](evidence/CW-05i.md). Accessible
in-game custom/native selection is the next CW-05 slice.
