using System;
using MphRead.Mods.Render;

int checks = 0;
void Check(bool result, string name)
{
    if (!result) throw new InvalidOperationException(name);
    checks++;
}
void RejectUnchanged(RetainedAtlasPageOwnership page, Action operation, string name,
    Type exceptionType)
{
    var before = (page.ReservedBytes, page.LiveBytes, page.LiveAllocations, page.IsEmpty);
    bool rejected = false;
    try { operation(); }
    catch (Exception ex) when (exceptionType.IsInstanceOfType(ex)) { rejected = true; }
    Check(rejected && before == (page.ReservedBytes, page.LiveBytes, page.LiveAllocations, page.IsEmpty), name);
}

bool invalidCapacity = false;
try { _ = new RetainedAtlasPageOwnership(0); }
catch (ArgumentOutOfRangeException) { invalidCapacity = true; }
Check(invalidCapacity, "zero-capacity atlas page rejected");

var page = new RetainedAtlasPageOwnership(100);
Check(page.ReservedBytes == 100 && page.LiveBytes == 0 && page.LiveAllocations == 0 && page.IsEmpty,
    "new page has reserved storage without live owners");
page.Retain(10);
page.Retain(20);
page.Retain(25);
Check(page.LiveBytes == 55 && page.LiveAllocations == 3 && !page.IsEmpty,
    "shared page counts every live immutable allocation");
RejectUnchanged(page, () => page.Retain(0), "zero-byte retain is atomic", typeof(ArgumentOutOfRangeException));
RejectUnchanged(page, () => page.Retain(46), "capacity overflow is atomic", typeof(ArgumentOutOfRangeException));
RejectUnchanged(page, () => page.Release(0), "zero-byte release is atomic", typeof(InvalidOperationException));
RejectUnchanged(page, () => page.Release(56), "ownership underflow is atomic", typeof(InvalidOperationException));
RejectUnchanged(page, () => page.Release(55), "partial-page release cannot remove all live bytes",
    typeof(InvalidOperationException));
Check(!page.Release(20) && page.LiveBytes == 35 && page.LiveAllocations == 2 && page.ReservedBytes == 100,
    "deleting one shared mesh preserves its page and sibling offsets");
page.Retain(15);
Check(!page.Release(10) && !page.Release(25) && page.LiveBytes == 15 && page.LiveAllocations == 1,
    "replacement can share a page while removed allocations stay unowned");
RejectUnchanged(page, () => page.Release(5), "final-allocation size mismatch is atomic",
    typeof(InvalidOperationException));
Check(page.Release(15) && page.LiveBytes == 0 && page.LiveAllocations == 0,
    "last mesh deletion signals page retirement exactly once");
RejectUnchanged(page, () => page.Release(15), "retired page rejects double release without changing state",
    typeof(InvalidOperationException));

// Repeated document lifetimes exercise production ownership; physical buffer
// release/in-flight safety is a separate native backend validation requirement.
int retirements = 0;
for (int cycle = 0; cycle < 24; cycle++)
{
    var shared = new RetainedAtlasPageOwnership(20UL * 1024 * 1024);
    foreach (ulong bytes in new ulong[] { 1024, 4096, 512, 8192 }) shared.Retain(bytes);
    foreach (ulong bytes in new ulong[] { 4096, 1024, 8192, 512 })
        if (shared.Release(bytes)) retirements++;
    Check(shared.IsEmpty && shared.LiveBytes == 0 && shared.LiveAllocations == 0,
        "document teardown releases all logical atlas owners");
}
Check(retirements == 24, "each document page retires once after its last owner");

var maximum = new RetainedAtlasPageOwnership(ulong.MaxValue);
maximum.Retain(ulong.MaxValue);
RejectUnchanged(maximum, () => maximum.Retain(1), "maximum-size capacity cannot wrap",
    typeof(ArgumentOutOfRangeException));
Check(maximum.Release(ulong.MaxValue) && maximum.LiveBytes == 0,
    "maximum-size ownership releases without arithmetic wrap");
Console.WriteLine($"RETAINEDATLAS {checks} production ownership checks passed.");
