using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace CadHarness.Playground;

public static class PlaygroundHost
{
    public static WebApplication Build(string[] args, INativeSession? fakeNative = null, IProviderFactory? fakeProvider = null, int port = 5186)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
        builder.Logging.ClearProviders(); // Do not log request bodies, headers or credentials.
        builder.WebHost.UseUrls("http://127.0.0.1:" + port);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
        var provider = fakeProvider ?? new ProviderFactory();
        var native = fakeNative ?? new NativeSession(Path.Combine(builder.Environment.ContentRootPath, "artifacts", "playground-sessions"),
            Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE"));
        builder.Services.AddSingleton(_ => new PlaygroundSession(native, new StaDispatcher(), provider));
        var app = builder.Build();
        var session = app.Services.GetRequiredService<PlaygroundSession>();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            var origin = context.Request.Headers.Origin.ToString();
            if (!System.Net.IPAddress.TryParse(context.Request.Host.Host, out var host) || !System.Net.IPAddress.IsLoopback(host) ||
                (origin.Length > 0 && origin != "http://127.0.0.1:" + port))
            { context.Response.StatusCode = 403; await Send(context, session, ApiResult.Fail("request", "LOCAL_ORIGIN_REQUIRED", "Use the local Playground URL.")); return; }
            try { await next(context); }
            catch (Exception error) when (error is JsonException or BadHttpRequestException or OperationCanceledException)
            { if (!context.RequestAborted.IsCancellationRequested) await Send(context, session, ApiResult.Fail("request", "REQUEST_INVALID", "Invalid, oversized or cancelled JSON request.")); }
        });
        app.UseDefaultFiles(); app.UseStaticFiles();
        app.MapGet("/api/status", (HttpContext c) => Send(c, session, ApiResult.Ok("status", session.Status)));
        app.MapGet("/api/solidworks/status", (HttpContext c) => Send(c, session, ApiResult.Ok("status", session.Status)));
        app.MapGet("/api/capabilities", (HttpContext c) => Send(c, session, session.Capabilities()));
        app.MapGet("/api/state", (HttpContext c) => Send(c, session, ApiResult.Ok("state", session.State)));
        app.MapGet("/api/session/events", (HttpContext c) => Send(c, session, ApiResult.Ok("events", session.Events)));
        Post<LlmSettings>("/api/llm/configure", (value, _) => session.Configure(value));
        app.MapPost("/api/llm/clear", (HttpContext c) => RunRequest(c, session, session.Clear()));
        app.MapPost("/api/llm/test", (HttpContext c) => RunRequest(c, session, session.TestConnection(c.RequestAborted)));
        Post<IntentRequest>("/api/plan", (value, token) => session.Plan(value, false, token));
        Post<IntentRequest>("/api/edit/plan", (value, token) => session.Plan(value, true, token));
        Post<PlanRequest>("/api/preflight", (value, _) => session.Preflight(value, false));
        Post<PlanRequest>("/api/edit/preflight", (value, _) => session.Preflight(value, true));
        Post<ExecuteRequest>("/api/execute", (value, _) => session.Execute(value, false));
        Post<ExecuteRequest>("/api/edit/execute", (value, _) => session.Execute(value, true));
        app.MapPost("/api/solidworks/connect", (HttpContext c) => RunRequest(c, session, session.Connect()));
        app.MapPost("/api/part/close", (HttpContext c) => RunRequest(c, session, session.Close()));
        app.MapPost("/api/session/export", (HttpContext c) => RunRequest(c, session, session.Export()));
        app.MapFallback("/api/{**path}", async (HttpContext c) =>
        { c.Response.StatusCode = 404; await Send(c, session, ApiResult.Fail("request", "API_NOT_FOUND", "Unknown Playground endpoint.")); });
        app.Lifetime.ApplicationStopped.Register(() => (provider as IDisposable)?.Dispose());
        return app;

        void Post<T>(string route, Func<T, CancellationToken, Task<ApiResult>> action) => app.MapPost(route, async (HttpContext c) =>
        {
            if (!c.Request.HasJsonContentType()) { await Send(c, session, ApiResult.Fail("request", "REQUEST_INVALID", "application/json required.")); return; }
            using var document = await JsonDocument.ParseAsync(c.Request.Body, new JsonDocumentOptions { MaxDepth = 32 }, c.RequestAborted);
            RejectDuplicates(document.RootElement);
            var value = document.RootElement.Deserialize<T>(Wire.Options) ?? throw new JsonException();
            await RunRequest(c, session, action(value, c.RequestAborted));
        });

    }
    private static async Task RunRequest(HttpContext context, PlaygroundSession session, Task<ApiResult> action) => await Send(context, session, await action);
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject()) { if (!names.Add(field.Name)) throw new JsonException(); RejectDuplicates(field.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
    private static async Task Send(HttpContext context, PlaygroundSession session, ApiResult result)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(session.Serialize(result), context.RequestAborted);
    }
    public static async Task Main(string[] args)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("CAD_HARNESS_PLAYGROUND_PORT"), out var p) ? p : 5186;
        await using var app = Build(args, port: port);
        Console.WriteLine("SWCAD-Harness Playground\nhttp://127.0.0.1:" + port);
        await app.RunAsync();
    }
}

