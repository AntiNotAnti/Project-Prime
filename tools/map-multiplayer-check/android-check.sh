#!/usr/bin/env bash
set -euo pipefail

# Both SDK tools and emulator resolve this task-owned AVD registration.
# Never depend on a runner's implicit Android preferences directory.
export ANDROID_USER_HOME="$RUNNER_TEMP/prime-map-check-user"
export ANDROID_AVD_HOME="$ANDROID_USER_HOME/avd"
mkdir -p "$ANDROID_AVD_HOME"
avd_name="prime-map-check"
emulator_log="$RUNNER_TEMP/map-android-emulator.log"
boot_timeout=${PRIME_ANDROID_BOOT_TIMEOUT_SECONDS:-600}
if [[ ! "$boot_timeout" =~ ^[1-9][0-9]{0,3}$ ]] || (( boot_timeout > 1800 )); then
  echo 'PRIME_ANDROID_BOOT_TIMEOUT_SECONDS must be between 1 and 1800' >&2
  exit 1
fi

sdkmanager_bin="$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager"
avdmanager_bin="$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager"
if [[ ! -x "$sdkmanager_bin" ]]; then
  sdkmanager_bin="$(find "$ANDROID_HOME/cmdline-tools" -type f -path '*/bin/sdkmanager' -print 2>/dev/null | sort -V | tail -n 1)"
fi
if [[ ! -x "$avdmanager_bin" ]]; then
  avdmanager_bin="$(find "$ANDROID_HOME/cmdline-tools" -type f -path '*/bin/avdmanager' -print 2>/dev/null | sort -V | tail -n 1)"
fi
if [[ -z "$sdkmanager_bin" || ! -x "$sdkmanager_bin" || -z "$avdmanager_bin" || ! -x "$avdmanager_bin" ]]; then
  echo "Android command-line tools were not found under $ANDROID_HOME/cmdline-tools." >&2
  exit 1
fi

"$sdkmanager_bin" --sdk_root="$ANDROID_HOME" platform-tools 'system-images;android-36;google_apis;x86_64' emulator
adb_bin="$ANDROID_HOME/platform-tools/adb"
if [[ ! -x "$adb_bin" ]]; then
  adb_bin="$(command -v adb || true)"
fi
if [[ -z "$adb_bin" || ! -x "$adb_bin" ]]; then
  echo "adb was not found after installing Android platform-tools." >&2
  exit 1
fi
printf 'no\n' | "$avdmanager_bin" create avd --force --name "$avd_name" --path "$ANDROID_AVD_HOME/$avd_name.avd" --package 'system-images;android-36;google_apis;x86_64'
"$ANDROID_HOME/emulator/emulator" -list-avds | tee "$RUNNER_TEMP/map-android-avds.txt"
if ! grep -Fxq "$avd_name" "$RUNNER_TEMP/map-android-avds.txt"; then
  echo "Emulator cannot resolve the newly created owned AVD: $avd_name" >&2
  exit 1
fi
if [[ -e /dev/kvm ]]; then sudo chmod 666 /dev/kvm; fi
{ ls -l /dev/kvm 2>/dev/null || true; "$ANDROID_HOME/emulator/emulator" -accel-check || true; } > "$RUNNER_TEMP/map-android-acceleration.txt" 2>&1
cat "$RUNNER_TEMP/map-android-acceleration.txt"
"$ANDROID_HOME/emulator/emulator" -avd "$avd_name" -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect > "$emulator_log" 2>&1 &
emulator_pid=$!
cleanup_emulator() {
  exit_status=$?
  if [[ "$exit_status" != 0 ]]; then
    echo "Android emulator/device admission failed; retained log: $emulator_log" >&2
    tail -n 100 "$emulator_log" >&2 || true
    timeout 5 "$adb_bin" devices -l >&2 || true
  fi
  kill "$emulator_pid" 2>/dev/null || true
  cleanup_deadline=$((SECONDS + 5))
  while kill -0 "$emulator_pid" 2>/dev/null && (( SECONDS < cleanup_deadline )); do
    sleep 1
  done
  if jobs -pr | grep -Fxq "$emulator_pid" && kill -0 "$emulator_pid" 2>/dev/null; then
    kill -KILL "$emulator_pid" 2>/dev/null || true
  fi
  wait "$emulator_pid" 2>/dev/null || true
}
trap cleanup_emulator EXIT

# A dead emulator cannot leave an unbounded adb wait hiding its stderr.
# Boot remains a positive gate: both a live process and sys.boot_completed=1.
booted=false
boot_deadline=$((SECONDS + boot_timeout))
while (( SECONDS < boot_deadline )); do
  if ! kill -0 "$emulator_pid" 2>/dev/null; then
    echo 'Android emulator exited before device boot completed' >&2
    exit 1
  fi
  state=$(timeout 5 "$adb_bin" get-state 2>/dev/null || true)
  if [[ "$state" == device ]]; then
    boot=$(timeout 5 "$adb_bin" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || true)
    if [[ "$boot" == 1 ]] && kill -0 "$emulator_pid" 2>/dev/null; then
      booted=true
      break
    fi
  fi
  sleep 2
done
if [[ "$booted" != true ]]; then
  echo "Android emulator did not present a booted device within $boot_timeout seconds" >&2
  exit 1
fi
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Debug -r android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidUseSharedRuntime=false -p:AndroidSdkDirectory="$ANDROID_HOME" -o "$RUNNER_TEMP/map-android-apk"
"$adb_bin" install -r "$RUNNER_TEMP"/map-android-apk/*-Signed.apk
"$adb_bin" shell am start -W -n com.projectprime.game/com.projectprime.game.MapAcceptanceActivity
for attempt in $(seq 1 120); do
  if "$adb_bin" shell run-as com.projectprime.game cat files/map-runtime-check.txt > "$RUNNER_TEMP/map-android-check.txt" 2>/dev/null; then
    cat "$RUNNER_TEMP/map-android-check.txt"
    grep -q '^PASS:' "$RUNNER_TEMP/map-android-check.txt"
    break
  fi
  sleep 2
done
test -s "$RUNNER_TEMP/map-android-check.txt" || { echo 'Android map runtime acceptance timed out' >&2; exit 1; }
