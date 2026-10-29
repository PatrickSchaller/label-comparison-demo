using System.Net;
using System.Text;
using System.Text.Json;
using LabelReview;
using Xunit;

namespace LabelReview.Tests;

public class AiExtractorTests
{
    private static byte[] Response(string status = "completed", string? text = null, string type = "output_text") => JsonSerializer.SerializeToUtf8Bytes(new
    {
        id = "resp_fixture", model = "test-model", status,
        output = new[] { new { type = "message", content = new[] { new { type, text = text ?? JsonSerializer.Serialize(ComparisonTests.Label(), Comparison.Json) } } } }
    });

    [Fact]
    public void CompleteStructuredResponseRetainsProvenance()
    {
        var result = AiExtractor.ParseResponse(Response());
        Assert.Equal("resp_fixture", result.ResponseId);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(14, result.Label.Fields.Length);
    }

    [Fact]
    public void IncompleteAndRefusedResponsesDoNotBecomeComparisons()
    {
        Assert.Throws<InvalidDataException>(() => AiExtractor.ParseResponse(Response(status: "incomplete")));
        Assert.Throws<InvalidDataException>(() => AiExtractor.ParseResponse(Response(type: "refusal")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    public void MalformedProviderEnvelopeFailsClosed(string json) =>
        Assert.Throws<InvalidDataException>(() => AiExtractor.ParseResponse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void InvalidStructuredTextFailsClosed() =>
        Assert.Throws<InvalidDataException>(() => AiExtractor.ParseResponse(Response(text: "{\"fields\":[]}")));

    [Theory]
    [InlineData("sample.png", "input_image")]
    [InlineData("sample.pdf", "input_file")]
    public async Task RequestUsesSchemaNoStorageAndInlineDocument(string filename, string contentType)
    {
        var bytes = filename.EndsWith("pdf", StringComparison.Ordinal) ? "%PDF-1.7\nfixture"u8.ToArray() : new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = data.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("configured-model", root.GetProperty("model").GetString());
            Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            Assert.Equal(14, root.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").GetProperty("fields").GetProperty("items").GetProperty("properties").GetProperty("key").GetProperty("enum").GetArrayLength());
            Assert.Equal(contentType, root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("type").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Response()) };
        });
        using var http = new HttpClient(handler);
        var result = await new AiExtractor(http).ExtractAsync(LabelInput.Create(filename, bytes), "test-key", "configured-model", CancellationToken.None);
        Assert.Equal("resp_fixture", result.ResponseId);
    }

    [Fact]
    public async Task ProviderErrorDoesNotLeakProviderBody()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent("private-provider-detail") })));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new AiExtractor(http).ExtractAsync(
            LabelInput.Create("label.pdf", "%PDF-fixture"u8.ToArray()), "test-key", "model", CancellationToken.None));
        Assert.DoesNotContain("private-provider-detail", ex.Message);
    }

    [Fact]
    public async Task OversizedProviderResponseIsRejected()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[512 * 1024 + 1]) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AiExtractor(http).ExtractAsync(
            LabelInput.Create("label.pdf", "%PDF-fixture"u8.ToArray()), "test-key", "model", CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
