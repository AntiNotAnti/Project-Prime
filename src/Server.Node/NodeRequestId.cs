namespace ProjectPrime.Server.Node;

/// <summary>Assigns a bounded correlation identifier before rate limits or body work.</summary>
public static class NodeRequestId
{
    public const string Header = "X-Request-Id";
    internal const string ItemKey = "ProjectPrime.NodeRequestId";

    public static string Resolve(string? supplied)
        => Guid.TryParseExact(supplied, "D", out Guid id) && id != Guid.Empty
            ? id.ToString("D")
            : Guid.NewGuid().ToString("D");

    public static void Use(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            string id = Resolve(context.Request.Headers[Header].ToString());
            context.Items[ItemKey] = id;
            context.Response.Headers[Header] = id;
            await next(context);
        });
    }
}
