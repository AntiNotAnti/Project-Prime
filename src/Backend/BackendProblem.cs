namespace MphRead.Backend;

/// <summary>Small, stable RFC 9457-style error envelope for client contract
/// handling. The code is safe to persist/display; detail never contains
/// credentials or framework exception text.</summary>
public static class BackendProblem
{
    public static Task WriteAsync(HttpContext http, string code, string title, int status,
        CancellationToken cancellationToken = default)
        => Create(code, title, status).ExecuteAsync(http);

    public static IResult Create(string code, string title, int status)
        => Results.Problem(statusCode: status, title: title,
            type: "https://project-prime/errors/" + code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
