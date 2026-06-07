using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Armadillo.Detection;

/// <summary>Probes a running Ollama instance for its installed models. Best-effort, never throws.</summary>
public sealed class OllamaProbe
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public OllamaProbe(HttpClient? http = null, string? host = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var raw = host ?? Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://127.0.0.1:11434";
        if (!raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) raw = "http://" + raw;
        _baseUri = new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Returns the model names from <c>GET /api/tags</c>, or empty if Ollama isn't reachable.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<TagsResponse>(new Uri(_baseUri, "/api/tags"), ct)
                .ConfigureAwait(false);
            return resp?.Models?.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                   ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private sealed record TagsResponse([property: JsonPropertyName("models")] List<ModelEntry>? Models);
    private sealed record ModelEntry([property: JsonPropertyName("name")] string Name);
}
