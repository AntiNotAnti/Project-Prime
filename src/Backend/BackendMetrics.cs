using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MphRead.Backend;

public static class BackendMetrics
{
    private static readonly Meter Meter = new("ProjectPrime.Backend", "1.0.0");
    // These instruments intentionally accept only the finite operation/outcome
    // values used by the route owners.  No account, Node, match, token, IP, or
    // request payload is ever attached as a metric dimension.
    public static readonly Counter<long> AccountOperations =
        Meter.CreateCounter<long>("projectprime.backend.account.operations");
    public static readonly Counter<long> DirectoryOperations =
        Meter.CreateCounter<long>("projectprime.backend.directory.operations");
    public static readonly Counter<long> AdmissionOperations =
        Meter.CreateCounter<long>("projectprime.backend.admission.operations");
    public static readonly Counter<long> CareerQueries =
        Meter.CreateCounter<long>("projectprime.backend.career.queries");
    public static readonly Counter<long> RequestRejections =
        Meter.CreateCounter<long>("projectprime.backend.request.rejections");
    public static readonly Histogram<double> MatchIngestionDuration =
        Meter.CreateHistogram<double>("projectprime.match_ingestion.duration", "ms");
    public static readonly Counter<long> MatchIngestionFailures =
        Meter.CreateCounter<long>("projectprime.match_ingestion.failures");
    public static readonly Counter<long> AcceptedMatches =
        Meter.CreateCounter<long>("projectprime.match_ingestion.accepted");
    public static readonly Histogram<long> AcceptedReportPayloadBytes =
        Meter.CreateHistogram<long>("projectprime.match_ingestion.accepted_payload_bytes", "bytes");

    public static void RecordAccount(string operation, string outcome)
        => AccountOperations.Add(1, Tags(operation, outcome));

    public static void RecordDirectory(string operation, string outcome)
        => DirectoryOperations.Add(1, Tags(operation, outcome));

    public static void RecordAdmission(string operation, string outcome)
        => AdmissionOperations.Add(1, Tags(operation, outcome));

    public static void RecordCareer(string operation, string outcome)
        => CareerQueries.Add(1, Tags(operation, outcome));

    public static void RecordRejection(string boundary, string reason)
        => RequestRejections.Add(1, Tags(boundary, reason));

    private static KeyValuePair<string, object?>[] Tags(string operation, string outcome)
        => [new("operation", operation), new("outcome", outcome)];

    public static MatchIngestionMeasurement Begin(int payloadBytes)
        => new(payloadBytes);

    public sealed class MatchIngestionMeasurement(int payloadBytes) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _statusCode;
        private bool _completed;

        public MatchIngestionMeasurement()
            : this(0) { }

        public void Complete(int statusCode)
        {
            _statusCode = statusCode;
            _completed = true;
        }

        public void Dispose()
        {
            double elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            MatchIngestionDuration.Record(elapsed);
            if (!_completed || _statusCode is < 200 or >= 300)
                MatchIngestionFailures.Add(1, new KeyValuePair<string, object?>("classification",
                    _completed ? (_statusCode == 409 ? "conflict" : "rejected") : "exception"));
            else if (_statusCode == 201)
            {
                AcceptedMatches.Add(1);
                AcceptedReportPayloadBytes.Record(payloadBytes);
            }
        }
    }
}
