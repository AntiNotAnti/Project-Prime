using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ProjectPrime.Server.Node;

/// <summary>Bounded Node telemetry for the control-plane owners.</summary>
public static class NodeMetrics
{
    private static readonly Meter Meter = new("ProjectPrime.Server.Node", "1.0.0");
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

    private static KeyValuePair<string, object?>[] Tags(string operation, string outcome)
        => [new("operation", operation), new("outcome", outcome)];
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
}
