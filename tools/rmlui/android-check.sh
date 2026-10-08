#!/usr/bin/env bash
set -euo pipefail

# Own only this CI emulator. Native framework fixtures do not require or ship
# extracted cartridge data; actual cartridge gameplay is a separate local run.
apk=${1:?usage: android-check.sh <native validation Signed.apk>}
: "${ANDROID_HOME:?Android SDK is required}"
: "${RUNNER_TEMP:?An isolated runner temporary directory is required}"
export ANDROID_USER_HOME="$RUNNER_TEMP/prime-rmlui-android-user"
export ANDROID_AVD_HOME="$ANDROID_USER_HOME/avd"
mkdir -p "$ANDROID_AVD_HOME"
sdkmanager_bin="$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager"
avdmanager_bin="$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager"
adb_bin="$ANDROID_HOME/platform-tools/adb"
"$sdkmanager_bin" --sdk_root="$ANDROID_HOME" 'system-images;android-36;google_apis;x86_64' emulator
printf 'no\n' | "$avdmanager_bin" create avd --force --name prime-rmlui-check \
  --path "$ANDROID_AVD_HOME/prime-rmlui-check.avd" --package 'system-images;android-36;google_apis;x86_64'
if [[ -e /dev/kvm ]]; then sudo chmod 666 /dev/kvm; fi
"$ANDROID_HOME/emulator/emulator" -avd prime-rmlui-check -port 5560 -no-window -no-audio \
  -no-boot-anim -gpu swiftshader_indirect > "$RUNNER_TEMP/rmlui-android-emulator.log" 2>&1 &
emulator_pid=$!
cleanup() {
  status=$?
  kill "$emulator_pid" 2>/dev/null || true
  deadline=$((SECONDS + 5))
  while kill -0 "$emulator_pid" 2>/dev/null && (( SECONDS < deadline )); do sleep 1; done
  if kill -0 "$emulator_pid" 2>/dev/null; then kill -KILL "$emulator_pid" 2>/dev/null || true; fi
  wait "$emulator_pid" 2>/dev/null || true
  exit "$status"
}
trap cleanup EXIT
booted=false
for attempt in $(seq 1 120); do
  if ! kill -0 "$emulator_pid" 2>/dev/null; then
    cat "$RUNNER_TEMP/rmlui-android-emulator.log" >&2
    exit 1
  fi
  boot=$(timeout 5 "$adb_bin" -s emulator-5560 shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || true)
  if [[ "$boot" == 1 ]]; then booted=true; break; fi
  sleep 2
done
if [[ "$booted" != true ]]; then
  echo 'The owned native UI emulator did not boot within 240 seconds' >&2
  exit 1
fi
python3 tools/rmlui/android-runtime-check.py --apk "$apk" --adb "$adb_bin" \
  --serial emulator-5560 --hud --output "$RUNNER_TEMP/rmlui-android-proof"
