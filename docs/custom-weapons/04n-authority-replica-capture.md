# CW-04n: bounded authority replica capture

This slice supplies an authority-thread value snapshot for recipient replication.
It does not enable spectator combat, send a packet, change the protocol, or finish
CW-04. Live fanout, baseline transfer/ACK and replica consumers follow separately.

`TryCaptureReplica` takes caller-owned spans with room for all eight slots and64
physical flights. Success copies eight explicit slot rows and the active flight
prefix, and returns match/epoch/manifest/world/counts. Failure returns default
metadata and leaves both caller spans unchanged. Flight tails are outside the
returned count. Fixed root scratch arrays stage the operation; no shared buffer or
mutable beam/pool/history object is returned. Calls belong outside controls,
emission, damage and all native rewind/shot scopes on the authority thread.

Each slot distinguishes occupied from absent, retains the actual generation/life
(including spectator Life0), and indicates whether a live custom journal exists.
Absent/unbound/spectating rows carry no invented weapon owner. A bound row copies
the journal's source-frontier preview and exact settled/committed metadata; it
does not transfer resources at world time or grant a loadout. Those rows are
public presentation data, never an input to the local action publisher.

Each flight retains its original full custom action identity and source
certificate, even when its shooter has respawned into a new life. Position,
spawn position, velocity and direction come from the actual private projectile,
including resolved hip spread. Velocity is displacement per simulation step;
age and remaining lifespan are seconds. A retained payment alone is insufficient:
only an actual noncollided physical projectile with a committed, nonexpired Flying
payment is included. Terminal player contacts, wall-impact effects, expired,
retired, uncertain and still-emitting payments cannot become baseline flights.

The payment observation API is pure: it checks the current store frame and
inclusive payment deadline without calling Advance, changing phase, consuming a
lease, expiring an entry, moving high-water marks or clearing quarantine. Capture
never calls OwnsFlight, CanReserveLaunch, BindLife, Process, SourceClock advancement
or canonical damage. Viewer obligations and original-owner private delivery remain
separate future responsibilities; capture does not reserve or release either.
