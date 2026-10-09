# Ordinary native networking smoke

`run-networking-slices.py` uses the existing real server, 2/4/8 native
`-netcheck -nographics` clients, and ordinary hitrig modes. It freezes the input
runtime into the evidence directory, copies only `paths.txt` into each isolated
process data directory, and never changes the operator's saved preferences.
The existing simulation/scenario and native weapon/source gates remain intact.
Only child processes owned by this invocation are signalled during cleanup.

```sh
python3 tools/hitrig/run-networking-slices.py \
  --runtime /absolute/path/to/built/runtime \
  --data /absolute/path/to/data-containing-paths-txt \
  --dotnet /absolute/path/to/dotnet \
  --output /tmp/networking-native-smoke \
  --seconds 30 --modes jump \
  --profiles rtt0-loss0,rtt100-loss2,rtt500-loss10 \
  --pause-after 8 --pause-seconds 4 --pause-profiles rtt100-loss2
```

The full seeded grid contains RTTs 0, 25, 50, 100, 150, 250, 350, and 500 ms crossed
with 0%, 1%, 2%, 5%, and 10% loss each way. `--list-profiles` prints all 40
profiles without launching anything; `--profiles all` runs the full grid.
Non-LAN profiles include jitter up to `min(60, RTT/5)` ms each way, 2% reorder,
and 1% duplicate traffic. The seed defaults to 431; peer 1 uses seed + 1.

The optional POSIX pause suspends one owned client process, including its socket
worker, then resumes it after the specified gap. This models a brief application
suspension and buffered traffic; it is not a measured cellular-network trace.
Choose a duration that leaves active time after resume.

`manifest.json` retains the frozen binary hash, source hashes for the existing
scenario/native gates, selected profiles, and whether those files remained
unchanged during execution. `summary.json` retains exact commands, exit codes,
the original native reports, trigger attempts, authority-confirmed hit counts,
and actual pause timing. Raw logs remain beside each arm. `--require-combat`
additionally fails an arm with no authority-confirmed client hit. Ordinary
movement/trigger checks and confirmed combat evidence are reported separately.

These checks exercise real native gameplay simulation and UDP. They do not
certify graphics, a physical Android device, or public server deployment.

Use `--players 2|4|8` to set the population (default 2). `--impacts` explicitly
opts server and clients into `-liveimpacts -liveimpactdebug -impactprofile` and
writes `server-impacts.json` / `peerN-impacts.json` on shutdown. Those are ingress
and authority facts, not rendered-view evidence. Join them with
`tools/live-impact/report-native.py ROOT --output REPORT.json`; the report never
subtracts timestamps from different processes. The manifest records population,
flags, binary identity and the source freeze check.

The existing rig may locally arm a weapon that the authority has not admitted.
A trigger or local prediction is not proof of a legal server hit. Preserve and
investigate zero-confirmation arms; do not bypass weapon ownership or damage
validation to make an acceptance result pass.
