using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabelReview;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LabelReview.Tests;

public class ApiTests
{
    [Fact]
    public async Task TwoUploadsFlowThroughExtractionComparisonAndProvenance()
    {
        var handler = new Provider();
        await using var app = Factory(handler);
        using var client = Client(app);
        using var content = Uploads();
        var response = await client.PostAsync("/api/compare", content);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<ComparisonReport>();
        Assert.Equal("live", report!.Mode);
        Assert.Equal(2, handler.Calls);
        Assert.Equal("changed", report.Rows.Single(row => row.Key == "ingredients").Status);
        Assert.Equal("resp_test_1", report.Before.ResponseId);
        Assert.Equal("resp_test_2", report.After.ResponseId);
        Assert.Equal(Comparison.Hash("%PDF-fixture"u8.ToArray()), report.Before.Sha256);
        Assert.Equal("label.pdf", report.Before.Name);
    }

    [Fact]
    public async Task InvalidConsentFilesAndSignaturesNeverCallTheProvider()
    {
        var handler = new Provider();
        await using var app = Factory(handler);
        using var client = Client(app);
        using var noConsent = Uploads(consent: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/compare", noConsent)).StatusCode);
        using var wrongType = Uploads(valid: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/compare", wrongType)).StatusCode);
        using var duplicates = Uploads();
        duplicates.Add(new ByteArrayContent("%PDF-third"u8.ToArray()), "before", "third.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/compare", duplicates)).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task FailedSecondExtractionReturnsNoPartialComparisonAndReleasesTheSlot()
    {
        var handler = new Provider { FailSecond = true };
        await using var app = Factory(handler);
        using var client = Client(app);
        using var first = Uploads();
        var response = await client.PostAsync("/api/compare", first);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("private-provider-detail", await response.Content.ReadAsStringAsync());
        handler.FailSecond = false;
        using var retry = Uploads();
        (await client.PostAsync("/api/compare", retry)).EnsureSuccessStatusCode();
    }

    private static MultipartFormDataContent Uploads(bool consent = true, bool valid = true)
    {
        var form = new MultipartFormDataContent();
        if (consent) form.Add(new StringContent("send"), "consent");
        form.Add(new ByteArrayContent(valid ? "%PDF-fixture"u8.ToArray() : "invalid"u8.ToArray()), "before", "private-filename.pdf");
        form.Add(new ByteArrayContent("%PDF-other"u8.ToArray()), "after", "new.pdf");
        return form;
    }

    private static WebApplicationFactory<Program> Factory(Provider provider) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["OPENAI_API_KEY"] = "test-key", ["OPENAI_MODEL"] = "test-model" }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<HttpClient>();
                services.AddSingleton(_ => new HttpClient(provider));
                services.AddTransient<IStartupFilter, Loopback>();
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1") });
        client.DefaultRequestHeaders.Add("X-Label-Review", "1");
        return client;
    }

    private sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return nextMiddleware(); });
            next(app);
        };
    }

    private sealed class Provider : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool FailSecond { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailSecond && Calls == 2)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("private-provider-detail") });
            var label = Calls % 2 == 0 ? ComparisonTests.With(ComparisonTests.Label(), "ingredients", "Oat, cocoa") : ComparisonTests.Label();
            var body = new
            {
                id = $"resp_test_{Calls}", model = "test-model", status = "completed",
                output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(label, Comparison.Json) } } } }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
