#!/usr/bin/env bash
set -euo pipefail

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
adb_bin="$ANDROID_HOME/platform-tools/adb"
if [[ ! -x "$adb_bin" ]]; then
  adb_bin="$(command -v adb || true)"
fi
if [[ -z "$adb_bin" || ! -x "$adb_bin" ]]; then
  echo "adb was not found; install the Android platform-tools package." >&2
  exit 1
fi

"$sdkmanager_bin" --sdk_root="$ANDROID_HOME" 'system-images;android-36;google_apis;x86_64' emulator
printf 'no\n' | "$avdmanager_bin" create avd --force --name prime-render-check --package 'system-images;android-36;google_apis;x86_64'
if [[ -e /dev/kvm ]]; then sudo chmod 666 /dev/kvm; fi
"$ANDROID_HOME/emulator/emulator" -avd prime-render-check -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect > "$RUNNER_TEMP/renderer-android-emulator.log" 2>&1 &
emulator_pid=$!
trap 'kill "$emulator_pid" 2>/dev/null || true' EXIT
"$adb_bin" wait-for-device
for attempt in $(seq 1 120); do
  if [[ $("$adb_bin" shell getprop sys.boot_completed | tr -d '\r') == 1 ]]; then break; fi
  sleep 2
done
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Debug -r android-x64 -p:EmbedAssembliesIntoApk=true -p:AndroidUseSharedRuntime=false -p:AndroidSdkDirectory="$ANDROID_HOME" -o "$RUNNER_TEMP/renderer-android-apk"
"$adb_bin" install -r "$RUNNER_TEMP"/renderer-android-apk/*-Signed.apk
"$adb_bin" shell am start -W -n com.projectprime.game/com.projectprime.game.RendererAcceptanceActivity
for attempt in $(seq 1 60); do
  if "$adb_bin" shell run-as com.projectprime.game cat files/renderer-runtime-check.txt > "$RUNNER_TEMP/renderer-android-renderer.txt" 2>/dev/null; then
    cat "$RUNNER_TEMP/renderer-android-renderer.txt"
    grep -q '^PASS Vulkan' "$RUNNER_TEMP/renderer-android-renderer.txt"
    # Recreate the surface by backgrounding and returning to the activity.
    "$adb_bin" shell input keyevent KEYCODE_HOME
    sleep 3
    "$adb_bin" shell am start -W -n com.projectprime.game/com.projectprime.game.RendererAcceptanceActivity
    for resumed in $(seq 1 30); do
      "$adb_bin" shell run-as com.projectprime.game cat files/renderer-runtime-check.txt > "$RUNNER_TEMP/renderer-android-renderer.txt"
      if grep -Eq '^PASS Vulkan.*frames=([2-9]|[1-9][0-9]+)$' "$RUNNER_TEMP/renderer-android-renderer.txt"; then exit 0; fi
      sleep 1
    done
    cat "$RUNNER_TEMP/renderer-android-renderer.txt"
    echo 'Android Vulkan surface recreation failed' >&2
    exit 1
  fi
  sleep 2
done
"$adb_bin" logcat -d > "$RUNNER_TEMP/renderer-android-logcat.txt"
echo 'Android Vulkan surface acceptance timed out' >&2
exit 1
