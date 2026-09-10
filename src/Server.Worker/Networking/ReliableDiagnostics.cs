using System.Diagnostics;

namespace MphRead.Mods.Network
{
    public static class ReliableDiagnostics
    {
        public static string Describe(ReliableChannel channel)
        {
            var oldest = channel.OldestPending(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            return $"reason={channel.LastAdmissionFailure} pending={channel.PendingCount} pendingHighWater={channel.PendingHighWater} "
                + $"oldestSpan={channel.OldestPendingSpan} oldestType={oldest?.Type} oldestId={oldest?.Id} "
                + $"oldestAttempts={oldest?.SentAttempts} oldestAgeSeconds={oldest?.AgeSeconds:F3} capacityRejections={channel.CapacityRejections} "
                + $"ordinaryAdmissionRejections={channel.OrdinaryAdmissionRejections} criticalReserveUses={channel.CriticalReserveUses} "
                + $"criticalAdmissionRejections={channel.CriticalAdmissionRejections} retrySeconds={channel.RetrySeconds:F3} "
                + $"idSpanRejections={channel.IdSpanRejections} oversizedPayloadRejections={channel.OversizedPayloadRejections} "
                + $"invalidTypeRejections={channel.InvalidTypeRejections} retransmissions={channel.Retransmissions}";
        }
    }
}
