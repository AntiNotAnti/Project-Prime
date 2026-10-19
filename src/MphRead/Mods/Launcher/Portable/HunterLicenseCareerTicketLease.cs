#if MPHREAD_AVALONIA
using System;

namespace MphRead.Mods.Launcher;

internal static partial class HunterLicenseClient
{
    private static object? _careerTicketAccountLease;
    private static int _careerTicketGeneration;

    internal static bool TryGetNativeCareerTicket(uint clientId, object? expectedLease, out string ticket)
    {
        lock (CareerTicketLock)
        {
            ticket = "";
            if (!IsNativeAccountLeaseCurrent(expectedLease)) return false;
            if (!TryGetCareerTicket(clientId, out string current)
                || !ReferenceEquals(_careerTicketAccountLease, expectedLease)
                || !IsNativeAccountLeaseCurrent(expectedLease)) return false;
            ticket = current; return true;
        }
    }
    private static bool CareerTicketWorkCurrent(int generation)
    { lock (CareerTicketLock) return generation == _careerTicketGeneration; }

    // The real async owner publishes through this guarded transaction. Kept
    // source-linkable so held completions can exercise actual cache writes.
    internal static bool PublishCareerTicket(uint clientId, string ticket, DateTimeOffset expires,
        object lease, int generation)
    {
        lock (CareerTicketLock)
        {
            if (generation != _careerTicketGeneration || !IsNativeAccountLeaseCurrent(lease)) return false;
            _careerTicket = ticket; _careerTicketClientId = clientId; _careerTicketExpiresAt = expires;
            _careerTicketAccountLease = lease; _careerTicketRetryAt = default;
            return true;
        }
    }
}
#endif
