# Android performance acceptance capture and comparison

Project Prime emits a rolling `androidperf` report approximately every five
seconds while a match is rendering. The comparison helper consumes those reports
without changing game behavior or requiring any proprietary assets.

## Record two runs on the same device

Build/install baseline (`-p:AndroidPerfOptIn=false`) and an enrolled Android
Release APK from the same source. For each, use the same map, hunter, player/bot
count, graphic quality, FPS cap, ROM, device power state, and Android version.
Start log capture before each test:

```sh
adb logcat -c
adb logcat -v time > baseline.log
# Finish the controlled match, then stop logcat.
adb logcat -c
adb logcat -v time > candidate.log
```

Run warmup and a minimum 30-minute session, repeat across cold launches and
pause/resume, and use a high-refresh phone plus memory-limited Adreno/Mali
devices. Observe actual scene image parity, AOT first-use stalls, GL errors,
audio pitch, battery, thermals and context recovery separately.

```sh
python3 tools/android-perf-compare.py --self-test
python3 tools/android-perf-compare.py --baseline baseline.log --candidate candidate.log \
    --minimum-windows 60 --max-p99-regression-pct 5 --json-out android-result.json
```

The comparator fails on incompatible render scales/caps, refresh rates, too
few windows and elevated **median window p99**, returning 0 success, 1 regression
or 2 invalid evidence. It also reports worst-window p99 and >20/33/50 ms rates.
Because the input consists of already aggregated window percentiles, the
comparison **does not** claim to reconstruct a global raw-frame p99.
A green report is not a release certification: image/audio/lifecycle correctness
and sustained thermal performance must pass on real devices.
