using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ProjectPrime.Server.Node;

public readonly record struct NodeWorkerMetricSnapshot(long ActiveMatches,
    long WorkerRetainedMatches, long SchedulerRetainedPlacements,
    long TerminalRetainedMatches,
    double OldestTerminalAgeSeconds, double HistoryUtilization,
    long ActiveAdmissions, long PendingAdmissions);

/// <summary>Bounded Node telemetry for the control-plane owners.</summary>
public static class NodeMetrics
{
    private static readonly Meter Meter = new("ProjectPrime.Server.Node", "1.0.0");
    public static readonly ActivitySource Activities = new("ProjectPrime.Server.Node", "1.0.0");
    public static readonly Counter<long> SessionOperations =
        Meter.CreateCounter<long>("projectprime.node.session.operations");
    public static readonly Counter<long> DirectoryReports =
        Meter.CreateCounter<long>("projectprime.node.directory.reports");
    public static readonly Counter<long> WebSocketRejections =
        Meter.CreateCounter<long>("projectprime.node.websocket.rejections");
    public static readonly Counter<long> MapDownloads =
        Meter.CreateCounter<long>("projectprime.node.map.downloads");
    public static readonly Counter<long> WorkerOperations =
        Meter.CreateCounter<long>("projectprime.node.worker.operations");
    public static readonly Counter<long> ReadinessEvaluations =
        Meter.CreateCounter<long>("projectprime.node.readiness.evaluations");
    public static readonly UpDownCounter<long> ControlConnections =
        Meter.CreateUpDownCounter<long>("projectprime.node.control.connections");
    public static readonly Counter<long> ControlDisconnects =
        Meter.CreateCounter<long>("projectprime.node.control.disconnects");
    public static readonly Counter<long> ControlResumes =
        Meter.CreateCounter<long>("projectprime.node.control.resumes");
    public static readonly Counter<long> DeliveryOverflows =
        Meter.CreateCounter<long>("projectprime.node.control.delivery_overflows");
    public static readonly Histogram<double> MatchPreparationDuration =
        Meter.CreateHistogram<double>("projectprime.node.match.preparation.duration", "ms");
    public static readonly Counter<long> MatchPreparationFailures =
        Meter.CreateCounter<long>("projectprime.node.match.preparation.failures");
    public static readonly Histogram<double> HandoffDuration =
        Meter.CreateHistogram<double>("projectprime.node.match.handoff.duration", "ms");
    public static readonly Counter<long> HandoffFailures =
        Meter.CreateCounter<long>("projectprime.node.match.handoff.failures");
    public static readonly Counter<long> HandoffSuperseded =
        Meter.CreateCounter<long>("projectprime.node.match.handoff.superseded");
    public static readonly Histogram<double> RejoinDuration =
        Meter.CreateHistogram<double>("projectprime.node.match.rejoin.duration", "ms");
    public static readonly Counter<long> RejoinFailures =
        Meter.CreateCounter<long>("projectprime.node.match.rejoin.failures");
    public static readonly Histogram<double> TransitionDuration =
        Meter.CreateHistogram<double>("projectprime.node.transition.duration", "ms");
    public static readonly Counter<long> TransitionTimeouts =
        Meter.CreateCounter<long>("projectprime.node.transition.timeouts");
    public static readonly Histogram<double> AdmissionInstallDuration =
        Meter.CreateHistogram<double>("projectprime.node.admission.install.duration", "ms");
    public static readonly Counter<long> AdmissionInstallFailures =
        Meter.CreateCounter<long>("projectprime.node.admission.install.failures");
    public static readonly Counter<long> LifecycleOperations =
        Meter.CreateCounter<long>("projectprime.node.lifecycle.operations");
    public static readonly Counter<long> LifecycleEdges =
        Meter.CreateCounter<long>("projectprime.node.lifecycle.edges");
    private static Func<NodeWorkerMetricSnapshot>? _workerMetrics;
    private static Func<long>? _outboundDepth;
    private static readonly ObservableGauge<long> WorkerActiveMatches =
        Meter.CreateObservableGauge("projectprime.node.worker.active_matches",
            () => ObserveWorkerLong(snapshot => snapshot.ActiveMatches));
    private static readonly ObservableGauge<long> WorkerRetainedMatches =
        Meter.CreateObservableGauge("projectprime.node.worker.retained_matches",
            () => ObserveWorkerLong(snapshot => snapshot.WorkerRetainedMatches));
    private static readonly ObservableGauge<double> WorkerHistoryUtilization =
        Meter.CreateObservableGauge("projectprime.node.worker.history_utilization",
            () => ObserveWorkerDouble(snapshot => snapshot.HistoryUtilization));
    private static readonly ObservableGauge<long> SchedulerRetainedPlacements =
        Meter.CreateObservableGauge("projectprime.node.scheduler.retained_placements",
            () => ObserveWorkerLong(snapshot => snapshot.SchedulerRetainedPlacements));
    private static readonly ObservableGauge<long> SchedulerTerminalRetainedPlacements =
        Meter.CreateObservableGauge("projectprime.node.scheduler.terminal_retained_placements",
            () => ObserveWorkerLong(snapshot => snapshot.TerminalRetainedMatches));
    private static readonly ObservableGauge<double> SchedulerOldestTerminalAge =
        Meter.CreateObservableGauge("projectprime.node.scheduler.oldest_terminal_age_seconds",
            () => ObserveWorkerDouble(snapshot => snapshot.OldestTerminalAgeSeconds), "s");
    private static readonly ObservableGauge<long> AdmissionActive =
        Meter.CreateObservableGauge("projectprime.node.admission.active",
            () => ObserveWorkerLong(snapshot => snapshot.ActiveAdmissions));
    private static readonly ObservableGauge<long> AdmissionPending =
        Meter.CreateObservableGauge("projectprime.node.admission.pending",
            () => ObserveWorkerLong(snapshot => snapshot.PendingAdmissions));
    private static readonly ObservableGauge<long> ControlOutboundDepth =
        Meter.CreateObservableGauge("projectprime.node.control.outbound.depth",
            ObserveOutboundDepth);

    public static void Session(string operation, string outcome)
        => SessionOperations.Add(1, Tags(operation, outcome));

    public static void Directory(string operation, string outcome)
        => DirectoryReports.Add(1, Tags(operation, outcome));

    public static void Rejected(string boundary, string reason)
        => WebSocketRejections.Add(1, Tags(boundary, reason));

    public static void Map(string operation, string outcome)
        => MapDownloads.Add(1, Tags(operation, outcome));

    public static void Worker(string operation, string outcome)
        => WorkerOperations.Add(1, Tags(operation, outcome));

    public static void Readiness(string outcome)
        => ReadinessEvaluations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void Lifecycle(string operation, string outcome)
        => LifecycleOperations.Add(1, Tags(operation, outcome));

    public static void LifecycleEdge(string edge)
        => LifecycleEdges.Add(1, new KeyValuePair<string, object?>("edge", edge));

    public static Activity? StartActivity(string operation)
        => Activities.StartActivity(operation, ActivityKind.Internal);

    public static void ControlConnected(bool resumed)
    {
        ControlConnections.Add(1);
        if (resumed) ControlResumes.Add(1);
    }

    public static void ControlDisconnected(string reason)
    {
        ControlConnections.Add(-1);
        ControlDisconnects.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public static void DeliveryOverflow()
        => DeliveryOverflows.Add(1);

    public static void RegisterWorkerMetrics(Func<NodeWorkerMetricSnapshot> provider)
        => Volatile.Write(ref _workerMetrics, provider);

    public static void RegisterOutboundDepth(Func<long> provider)
        => Volatile.Write(ref _outboundDepth, provider);

    public static void RecordDuration(Histogram<double> instrument, TimeProvider clock,
        long started, string outcome, string? stage = null)
    {
        double milliseconds = clock.GetElapsedTime(started).TotalMilliseconds;
        if (stage == null)
            instrument.Record(milliseconds, new KeyValuePair<string, object?>("outcome", outcome));
        else
            instrument.Record(milliseconds, new KeyValuePair<string, object?>("outcome", outcome),
                new KeyValuePair<string, object?>("stage", stage));
    }

    private static KeyValuePair<string, object?>[] Tags(string operation, string outcome)
        => [new("operation", operation), new("outcome", outcome)];

    private static long ObserveWorkerLong(Func<NodeWorkerMetricSnapshot, long> select)
    {
        Func<NodeWorkerMetricSnapshot>? provider = Volatile.Read(ref _workerMetrics);
        return provider == null ? 0 : select(provider());
    }

    private static double ObserveWorkerDouble(Func<NodeWorkerMetricSnapshot, double> select)
    {
        Func<NodeWorkerMetricSnapshot>? provider = Volatile.Read(ref _workerMetrics);
        return provider == null ? 0 : select(provider());
    }

    private static long ObserveOutboundDepth()
        => Volatile.Read(ref _outboundDepth)?.Invoke() ?? 0;
}

/// <summary>
/// Stable categories and EventIds for Node diagnostics.  Messages deliberately
/// carry only finite operation/outcome values: Node, session, match, and worker
/// identifiers are not log dimensions.
/// </summary>
public static class NodeDiagnostics
{
    public const string SessionCategory = "ProjectPrime.Server.Node.Session";
    public const string DirectoryCategory = "ProjectPrime.Server.Node.Directory";
    public const string WebSocketCategory = "ProjectPrime.Server.Node.WebSocket";
    public const string WorkerCategory = "ProjectPrime.Server.Node.Worker";
    public const string MapCategory = "ProjectPrime.Server.Node.Map";
    public const string ReadinessCategory = "ProjectPrime.Server.Node.Readiness";

    public static readonly EventId SessionEvent = new(2001, "SessionOperation");
    public static readonly EventId DirectoryEvent = new(2002, "DirectoryReport");
    public static readonly EventId WebSocketRejectedEvent = new(2003, "WebSocketRejected");
    public static readonly EventId WorkerEvent = new(2004, "WorkerOperation");
    public static readonly EventId MapEvent = new(2005, "MapDownload");
    public static readonly EventId ReadinessEvent = new(2006, "ReadinessEvaluation");
    public static readonly EventId LifecycleEvent = new(2007, "LifecycleOperation");

    public static void Session(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(SessionEvent, "Node session operation {Operation} completed with {Outcome}.", operation, outcome);
        NodeMetrics.Session(operation, outcome);
    }

    public static void Directory(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(DirectoryEvent, "Node directory report {Operation} completed with {Outcome}.", operation, outcome);
        NodeMetrics.Directory(operation, outcome);
    }

    public static void Rejected(ILogger logger, string boundary, string reason)
    {
        logger.LogWarning(WebSocketRejectedEvent, "Node WebSocket request rejected at {Boundary}: {Reason}.", boundary, reason);
        NodeMetrics.Rejected(boundary, reason);
    }

    public static void Worker(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(WorkerEvent, "Node Worker operation {Operation} completed with {Outcome}.", operation, outcome);
        NodeMetrics.Worker(operation, outcome);
    }

    public static void Map(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(MapEvent, "Node map operation {Operation} completed with {Outcome}.", operation, outcome);
        NodeMetrics.Map(operation, outcome);
    }

    public static void Readiness(ILogger logger, string outcome)
    {
        logger.LogInformation(ReadinessEvent, "Node readiness evaluation completed with {Outcome}.", outcome);
        NodeMetrics.Readiness(outcome);
    }

    /// <summary>Emits one finite lifecycle operation outcome. Match identity
    /// is structured log context only and is never a metric label.</summary>
    public static void Lifecycle(ILogger logger, string operation, string outcome,
        Guid? matchId = null)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(LifecycleEvent,
                "Node lifecycle operation {Operation} observed with {Outcome} for match {MatchId}.",
                operation, outcome, matchId);
        NodeMetrics.Lifecycle(operation, outcome);
    }

    public static void LifecycleEdge(ILogger logger, string edge, Guid? matchId = null)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(LifecycleEvent,
                "Node lifecycle edge {Edge} observed for match {MatchId}.", edge, matchId);
        NodeMetrics.LifecycleEdge(edge);
    }
}
