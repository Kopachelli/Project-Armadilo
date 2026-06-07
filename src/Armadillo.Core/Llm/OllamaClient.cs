using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Armadillo.Core.Llm;

/// <summary>
/// Minimal local Ollama client for the brain's review + embedding paths. Local = $0 tokens and
/// private. Every call is best-effort: returns null when Ollama isn't running so the harness
/// degrades gracefully rather than failing.
/// </summary>
public sealed class OllamaClient
{
    private readonly HttpClient _http;
    private readonly Uri _base;

    public OllamaClient(HttpClient? http = null, string? host = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var raw = host ?? Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://127.0.0.1:11434";
        if (!raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) raw = "http://" + raw;
        _base = new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Non-streaming chat. <paramref name="json"/> requests JSON-formatted output.</summary>
    public async Task<string?> ChatAsync(string model, string system, string user, bool json = false,
        CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                model,
                stream = false,
                format = json ? "json" : null,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
            };
            var resp = await _http.PostAsJsonAsync(new Uri(_base, "/api/chat"), body, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct).ConfigureAwait(false);
            return doc?.Message?.Content;
        }
        catch { return null; }
    }

    /// <summary>Embed a single string; null if unavailable.</summary>
    public async Task<float[]?> EmbedAsync(string model, string text, CancellationToken ct = default)
    {
        try
        {
            var body = new { model, prompt = text };
            var resp = await _http.PostAsJsonAsync(new Uri(_base, "/api/embeddings"), body, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct).ConfigureAwait(false);
            return doc?.Embedding;
        }
        catch { return null; }
    }

    private sealed record ChatResponse([property: JsonPropertyName("message")] ChatMessage? Message);
    private sealed record ChatMessage([property: JsonPropertyName("content")] string? Content);
    private sealed record EmbedResponse([property: JsonPropertyName("embedding")] float[]? Embedding);
}
