using Microsoft.AspNetCore.Http;
using MphRead.Backend.Matches;

namespace MphRead.Backend;

/// <summary>Body and response boundaries are route contracts, not one global
/// exception. Kestrel is configured to the largest bounded route and the
/// request middleware applies the smaller endpoint bound before model binding.</summary>
public static class BackendRequestLimits
{
    public const int DefaultJsonBytes = 16 * 1024;
    public const int NodeRegistrationBytes = 64 * 1024;
    public const int MaximumKestrelRequestBytes = ReportValidation.MaximumBytes;

    public static int ForPath(PathString path)
        => path.Equals("/v1/server/matches", StringComparison.OrdinalIgnoreCase)
            ? ReportValidation.MaximumBytes
            : path.Equals("/v1/node/registration", StringComparison.OrdinalIgnoreCase)
                ? NodeRegistrationBytes
                : DefaultJsonBytes;
}
