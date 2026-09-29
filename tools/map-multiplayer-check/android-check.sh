#!/usr/bin/env bash
set -euo pipefail
sdkmanager 'system-images;android-36;google_apis;x86_64' emulator
printf 'no\n' | avdmanager create avd --force --name prime-map-check --package 'system-images;android-36;google_apis;x86_64'
if [[ -e /dev/kvm ]]; then sudo chmod 666 /dev/kvm; fi
"$ANDROID_HOME/emulator/emulator" -avd prime-map-check -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect > "$RUNNER_TEMP/map-android-emulator.log" 2>&1 &
emulator_pid=$!
trap 'kill "$emulator_pid" 2>/dev/null || true' EXIT
adb wait-for-device
for attempt in $(seq 1 120); do
  if [[ $(adb shell getprop sys.boot_completed | tr -d '\r') == 1 ]]; then break; fi
  sleep 2
done
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Debug -r android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidUseSharedRuntime=false -p:AndroidSdkDirectory="$ANDROID_HOME" -o "$RUNNER_TEMP/map-android-apk"
adb install -r "$RUNNER_TEMP"/map-android-apk/*-Signed.apk
adb shell am start -W -n com.projectprime.game/com.projectprime.game.MapAcceptanceActivity
for attempt in $(seq 1 120); do
  if adb shell run-as com.projectprime.game cat files/map-runtime-check.txt > "$RUNNER_TEMP/map-android-check.txt" 2>/dev/null; then
    cat "$RUNNER_TEMP/map-android-check.txt"
    grep -q '^PASS:' "$RUNNER_TEMP/map-android-check.txt"
    exit $?
  fi
  sleep 2
done
adb logcat -d > "$RUNNER_TEMP/map-android-logcat.txt"
echo 'Android map runtime acceptance timed out' >&2
exit 1
