#!/usr/bin/env bash
set -euo pipefail

# Every potentially blocking stage is bounded and named. A single opaque CI
# step must not sit for half an hour in adb wait-for-device or an SDK download.
stage() { printf '[android-map-check %s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
fail() { stage "ERROR: $*"; exit 1; }
adb_bin=""
emulator_pid=0
cleanup() {
  code=$?
  trap - EXIT
  if (( code != 0 )); then
    stage "Failed with exit $code; collecting emulator and adb diagnostics."
    if [[ -n "$adb_bin" && -x "$adb_bin" ]]; then
      timeout 10s "$adb_bin" devices -l || true
      timeout 20s "$adb_bin" logcat -d -t 300 > "$RUNNER_TEMP/map-android-logcat.txt" 2>&1 || true
      tail -n 40 "$RUNNER_TEMP/map-android-logcat.txt" 2>/dev/null || true
    fi
    if [[ -f "$RUNNER_TEMP/map-android-emulator.log" ]]; then
      stage "Emulator log (last 80 lines):"
      tail -n 80 "$RUNNER_TEMP/map-android-emulator.log" || true
    fi
  fi
  if (( emulator_pid > 0 )); then
    kill "$emulator_pid" 2>/dev/null || true
    wait "$emulator_pid" 2>/dev/null || true
  fi
  exit "$code"
}
trap cleanup EXIT

sdkmanager_bin="$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager"
avdmanager_bin="$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager"
if [[ ! -x "$sdkmanager_bin" ]]; then
  sdkmanager_bin="$(find "$ANDROID_HOME/cmdline-tools" -type f -path '*/bin/sdkmanager' -print 2>/dev/null | sort -V | tail -n 1)"
fi
if [[ ! -x "$avdmanager_bin" ]]; then
  avdmanager_bin="$(find "$ANDROID_HOME/cmdline-tools" -type f -path '*/bin/avdmanager' -print 2>/dev/null | sort -V | tail -n 1)"
fi
[[ -n "$sdkmanager_bin" && -x "$sdkmanager_bin" && -n "$avdmanager_bin" && -x "$avdmanager_bin" ]] ||
  fail "Android command-line tools are missing from $ANDROID_HOME/cmdline-tools."

stage "Installing API 36 emulator image (8-minute upper bound)."
timeout --kill-after=10s 8m "$sdkmanager_bin" --sdk_root="$ANDROID_HOME" \
  platform-tools 'system-images;android-36;google_apis;x86_64' emulator ||
  fail "SDK emulator image installation failed or exceeded eight minutes."

adb_bin="$ANDROID_HOME/platform-tools/adb"
if [[ ! -x "$adb_bin" ]]; then
  adb_bin="$(command -v adb || true)"
fi
[[ -n "$adb_bin" && -x "$adb_bin" ]] || fail "adb was not found after installing platform-tools."

stage "Creating API 36 emulator in a deterministic AVD home."
# sdkmanager/avdmanager and emulator can pick different defaults for their
# config directories on GitHub-hosted runners. Use an explicit home, plus the
# AVD's absolute data path, and publish its .ini entry at that exact location.
# Without this, avdmanager reports success but emulator -avd cannot find it.
export ANDROID_AVD_HOME="$RUNNER_TEMP/prime-map-avds"
export ANDROID_USER_HOME="$RUNNER_TEMP/prime-map-android-user"
mkdir -p "$ANDROID_AVD_HOME" "$ANDROID_USER_HOME"
avd_directory="$ANDROID_AVD_HOME/prime-map-check.avd"
printf 'no\n' | timeout --kill-after=5s 60s "$avdmanager_bin" create avd --force \
  --name prime-map-check --path "$avd_directory" \
  --package 'system-images;android-36;google_apis;x86_64' ||
  fail "AVD creation failed or timed out."
[[ -s "$avd_directory/config.ini" ]] ||
  fail "avdmanager completed without creating $avd_directory/config.ini."
printf 'avd.ini.encoding=UTF-8\npath=%s\ntarget=android-36\n' "$avd_directory" \
  > "$ANDROID_AVD_HOME/prime-map-check.ini"
stage "Verifying emulator discovery of prime-map-check."
timeout --kill-after=5s 20s "$ANDROID_HOME/emulator/emulator" -list-avds \
  | tee "$RUNNER_TEMP/map-android-avd-list.txt"
grep -Fxq prime-map-check "$RUNNER_TEMP/map-android-avd-list.txt" ||
  fail "Emulator does not see prime-map-check despite the AVD manifest; refusing to wait for adb."

# Booting x86_64 without KVM is prohibitively slow on hosted CI. Make this
# runner prerequisite explicit rather than silently spinning in adb.
[[ -e /dev/kvm ]] || fail "Runner has no /dev/kvm; cannot run x86_64 emulator with hardware acceleration."
sudo chmod 666 /dev/kvm
stage "Starting headless emulator with KVM acceleration."
"$ANDROID_HOME/emulator/emulator" -avd prime-map-check -no-window -no-audio \
  -no-boot-anim -no-snapshot -no-metrics -accel on -gpu swiftshader_indirect \
  > "$RUNNER_TEMP/map-android-emulator.log" 2>&1 &
emulator_pid=$!

stage "Waiting for adb (90-second upper bound)."
timeout --kill-after=5s 90s "$adb_bin" wait-for-device ||
  fail "Emulator did not appear in adb within 90 seconds."

stage "Waiting for Android boot_completed (3-minute upper bound)."
booted=0
for attempt in $(seq 1 90); do
  kill -0 "$emulator_pid" 2>/dev/null || fail "Emulator process exited during boot."
  state="$(timeout 8s "$adb_bin" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r\n' || true)"
  if [[ "$state" == 1 ]]; then
    booted=1
    break
  fi
  if (( attempt % 15 == 0 )); then stage "Boot still pending ($attempt/90 checks)."; fi
  sleep 2
done
(( booted == 1 )) || fail "Android did not finish booting."
stage "Android boot completed."

stage "Building x64 Debug acceptance APK (8-minute upper bound)."
timeout --kill-after=10s 8m dotnet publish src/MphRead.Android/MphRead.Android.csproj \
  -c Debug -r android-x64 -p:EmbedAssembliesIntoApk=true \
  -p:AndroidUseSharedRuntime=false -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -o "$RUNNER_TEMP/map-android-apk" ||
  fail "Debug acceptance APK publish failed or exceeded eight minutes."

apk="$(find "$RUNNER_TEMP/map-android-apk" -maxdepth 1 -name '*-Signed.apk' -type f -print -quit)"
[[ -n "$apk" && -s "$apk" ]] || fail "Debug acceptance APK was not produced."
stage "Installing acceptance APK (2-minute upper bound)."
timeout --kill-after=5s 2m "$adb_bin" install -r "$apk" ||
  fail "Emulator failed to install the acceptance APK."

stage "Launching MapAcceptanceActivity."
timeout --kill-after=5s 45s "$adb_bin" shell am start -W \
  -n com.projectprime.game/com.projectprime.game.MapAcceptanceActivity ||
  fail "Could not start MapAcceptanceActivity."

stage "Waiting for the authoritative map-runtime-check result."
result=0
for attempt in $(seq 1 90); do
  if timeout 8s "$adb_bin" shell run-as com.projectprime.game \
    cat files/map-runtime-check.txt > "$RUNNER_TEMP/map-android-check.txt" 2>/dev/null; then
    cat "$RUNNER_TEMP/map-android-check.txt"
    grep -q '^PASS:' "$RUNNER_TEMP/map-android-check.txt" ||
      fail "The Android runtime acceptance activity did not report PASS."
    result=1
    break
  fi
  if (( attempt % 15 == 0 )); then stage "Map runtime acceptance pending ($attempt/90 checks)."; fi
  sleep 2
done
(( result == 1 )) || fail "Android map runtime acceptance timed out."
stage "Android package, download, registration and runtime checks passed."
