using System.Net;

namespace Immich.SmartOrganizer.Tests;

internal sealed class StubHttpMessageHandler(
    Func<CapturedHttpRequest, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    public List<CapturedHttpRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        CapturedHttpRequest captured = new(
            request.Method,
            request.RequestUri!,
            headers,
            body);
        Requests.Add(captured);
        return await send(captured, cancellationToken);
    }

    public static HttpResponseMessage Json(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body);
