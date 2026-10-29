using System.Net;
using System.Text.Json;
using LabelReview;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5098");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 13 * 1024 * 1024);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 13 * 1024 * 1024;
    // Keep small demo uploads in memory rather than spooling label content to a temporary file.
    options.MemoryBufferThreshold = 13 * 1024 * 1024;
});
builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(150) });
builder.Services.AddSingleton<AiExtractor>();
using var liveSlot = new SemaphoreSlim(1, 1);
var app = builder.Build();
var key = builder.Configuration["OPENAI_API_KEY"]?.Trim() ?? "";
var model = builder.Configuration["OPENAI_MODEL"]?.Trim() ?? "";
var liveEnabled = key.Length > 0 && model.Length > 0;

app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
        || context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "[::1]"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; frame-src blob:; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Method == "POST" && context.Request.Headers["X-Label-Review"] != "1")
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () => Results.Ok(new { liveEnabled, model = liveEnabled ? model : null }));
app.MapGet("/api/sample", async () =>
{
    var path = Path.Combine(app.Environment.WebRootPath, "samples", "extractions.json");
    var labels = JsonSerializer.Deserialize<LabelDocument[]>(await File.ReadAllTextAsync(path), Comparison.Json)!;
    var before = await File.ReadAllBytesAsync(Path.Combine(app.Environment.WebRootPath, "samples", "before.png"));
    var after = await File.ReadAllBytesAsync(Path.Combine(app.Environment.WebRootPath, "samples", "after.png"));
    return new ComparisonReport("sample", "Prepared extraction fixtures; no AI request was made. All label content is invented.",
        DateTimeOffset.UtcNow, new("before.png", Comparison.Hash(before), "fixture", "none"),
        new("after.png", Comparison.Hash(after), "fixture", "none"), Comparison.Compare(labels[0], labels[1]));
});

app.MapPost("/api/compare", async (HttpRequest request, AiExtractor ai, CancellationToken cancellationToken) =>
{
    if (!liveEnabled) return Results.Problem("Set OPENAI_API_KEY and OPENAI_MODEL on the server, then restart.", statusCode: 503);
    if (!request.HasFormContentType) return Results.Problem("Send two label files as multipart form data.", statusCode: 400);
    if (!await liveSlot.WaitAsync(0, cancellationToken))
        return Results.Problem("A live comparison is already running. Wait for it to finish.", statusCode: 429);
    try
    {
        var form = await request.ReadFormAsync(cancellationToken);
        if (form["consent"] != "send")
            return Results.Problem("Confirm that both labels may be sent to OpenAI before comparing.", statusCode: 400);
        if (form.Files.Count != 2 || form.Files.Count(f => f.Name == "before") != 1 || form.Files.Count(f => f.Name == "after") != 1)
            return Results.Problem("Choose exactly one previous label and one new label.", statusCode: 400);
        var before = await ReadInput(form.Files.GetFile("before")!, cancellationToken);
        var after = await ReadInput(form.Files.GetFile("after")!, cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var oldExtraction = await ai.ExtractAsync(before, key, model, deadline.Token);
        var newExtraction = await ai.ExtractAsync(after, key, model, deadline.Token);
        return Results.Ok(new ComparisonReport("live", "AI-extracted evidence with deterministic comparison. Check both original labels; this is not an approval or compliance assessment.",
            DateTimeOffset.UtcNow,
            new(before.Name, Comparison.Hash(before.Bytes), oldExtraction.ResponseId, oldExtraction.Model),
            new(after.Name, Comparison.Hash(after.Bytes), newExtraction.ResponseId, newExtraction.Model),
            Comparison.Compare(oldExtraction.Label, newExtraction.Label)));
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("AI extraction timed out. Try clearer or smaller labels.", statusCode: 504);
    }
    catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
    {
        return Results.Problem(ex is InvalidDataException ? ex.Message : "Upload is too large or malformed.", statusCode: 400);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("AI service could not complete the extraction. Check server credentials, model access, network and quota. No comparison was produced.", statusCode: 502);
    }
    finally { liveSlot.Release(); }
});

app.Run();

static async Task<LabelInput> ReadInput(IFormFile file, CancellationToken token)
{
    if (file.Length is <= 0 or > LabelInput.MaxBytes)
        throw new InvalidDataException("Each label must be between 1 byte and 6 MiB.");
    await using var stream = file.OpenReadStream();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer, token);
    return LabelInput.Create(file.FileName, buffer.ToArray());
}

public partial class Program;
