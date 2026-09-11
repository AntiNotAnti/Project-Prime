using Microsoft.Extensions.Logging;

namespace MphRead.Backend;

/// <summary>
/// Stable, low-cardinality Backend log categories and event IDs.  Callers pass
/// only fixed operation/outcome names; identifiers and credential material stay
/// out of both logs and metrics.
/// </summary>
public static class BackendDiagnostics
{
    public const string AccountCategory = "ProjectPrime.Backend.Account";
    public const string DirectoryCategory = "ProjectPrime.Backend.Directory";
    public const string AdmissionCategory = "ProjectPrime.Backend.Admission";
    public const string CareerCategory = "ProjectPrime.Backend.Career";
    public const string RequestCategory = "ProjectPrime.Backend.Request";

    public static readonly EventId AccountEvent = new(1001, "AccountOperation");
    public static readonly EventId DirectoryEvent = new(1002, "DirectoryOperation");
    public static readonly EventId AdmissionEvent = new(1003, "AdmissionOperation");
    public static readonly EventId CareerEvent = new(1004, "CareerQuery");
    public static readonly EventId RequestRejectedEvent = new(1005, "RequestRejected");

    public static void Account(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(AccountEvent, "Account operation {Operation} completed with {Outcome}.", operation, outcome);
        BackendMetrics.RecordAccount(operation, outcome);
    }

    public static void Directory(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(DirectoryEvent, "Directory operation {Operation} completed with {Outcome}.", operation, outcome);
        BackendMetrics.RecordDirectory(operation, outcome);
    }

    public static void Admission(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(AdmissionEvent, "Admission operation {Operation} completed with {Outcome}.", operation, outcome);
        BackendMetrics.RecordAdmission(operation, outcome);
    }

    public static void Career(ILogger logger, string operation, string outcome)
    {
        logger.LogInformation(CareerEvent, "Career query {Operation} completed with {Outcome}.", operation, outcome);
        BackendMetrics.RecordCareer(operation, outcome);
    }

    public static void Rejected(ILogger logger, string boundary, string reason)
    {
        logger.LogWarning(RequestRejectedEvent, "Request rejected at {Boundary}: {Reason}.", boundary, reason);
        BackendMetrics.RecordRejection(boundary, reason);
    }
}
