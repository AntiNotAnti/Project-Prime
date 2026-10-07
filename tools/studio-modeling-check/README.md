# Studio modeling checks

Run `dotnet run --project tools/studio-modeling-check -c Release` with .NET 10.
The fixture project references the canonical engine; it does not copy its modeling
implementation. Fixtures use actual `MapMesh`, topology/validator/compiler and
`MapDocument` history, with no game assets or native graphics required.

Checks exercise quad ring cuts, multiple/offset cuts, shared-vertex world-plane
knife cuts over convex and concave closed meshes, segmented boundary bridging,
Coons quad grid filling, constant-width planar region inset, general closed
endpoint-fan bevel, connected/spatial proportional falloff, ordered mirror/
array evaluation, channel preservation, source byte immutability and transactional
undo/redo/rejection. The 128-island workload reports actual CPU and allocation.
The suite currently has 121 assertions. Its measured workload must stay below
64 MiB of allocated managed memory while producing a validated closed mesh;
reported timing is a measurement, not a fixed pass threshold. The same tool can
run with `-p:MphReadServer=true` to verify the canonical module in the headless
engine build.

Loop cuts require an uninterrupted quad strip. Bridging requires equally sized
simple disjoint boundary loops. Grid fill requires one planar convex even loop.
Constant-width inset requires a convex planar region and preserves its boundary
with a world-space offset, including nonuniform object transforms. Bevel clips
the entire manifold endpoint fan and caps it; it rejects open fans, excessive
width and the canonical polygon corner limit. Mirror requires source geometry on
one side of its local plane; welding removes
plane caps and shares seam vertices. Knife retains both sides, allowing a later
selection/separation step. These restrictions are enforced before a live document
is changed. `WithModifierStack` stores resolved runtime geometry and one original
source with an ordered JSON-polymorphic modifier list. Editing the stack
reevaluates that source. Raw topology editing requires an explicit bake, so
provenance cannot silently become inconsistent with the evaluated mesh.
All ten proposal APIs accept the central job cancellation token. A deterministic
worker fixture cancels only after its first real 128-copy modifier completes;
it verifies the matching cancellation exception, no late adoption, byte-exact
source preservation and unchanged canonical document/history. A pre-cancelled
token is also checked at every public proposal entry point.

The actual `MapDocument.PaintFaces` boundary rejects unbaked stacked geometry
before changing either serialized document content or history state.
