namespace Armadillo.Core.Providers;

public enum ProviderKind { Anthropic, OpenAI, Google, Local }

/// <summary>
/// Decouples "which CLI binary" from "which model/provider endpoint". The same adapter can target
/// cloud Anthropic/OpenAI/Google, z.ai/GLM, Qwen, or a local OpenAI-compatible server by swapping
/// this and the env it injects at spawn. Delivers privacy (local), cost (cheap clouds), and
/// flexibility through one seam.
/// </summary>
public sealed record ProviderProfile(
    string Name,
    ProviderKind Kind,
    string? BaseUrl = null,
    string? ApiKey = null,
    string? Model = null)
{
    /// <summary>Inherit the host tool's existing login/config — spawn with no provider overrides.</summary>
    public static readonly ProviderProfile Inherit = new("inherit", ProviderKind.Anthropic);

    /// <summary>Environment variables to inject into the child for this provider.</summary>
    public IReadOnlyDictionary<string, string> EnvOverrides()
    {
        var env = new Dictionary<string, string>();
        if (Name == "inherit") return env;

        switch (Kind)
        {
            case ProviderKind.Anthropic:
                if (BaseUrl is not null) env["ANTHROPIC_BASE_URL"] = BaseUrl;
                if (ApiKey is not null) env["ANTHROPIC_API_KEY"] = ApiKey;
                break;
            case ProviderKind.OpenAI:
            case ProviderKind.Local:
                if (BaseUrl is not null) env["OPENAI_BASE_URL"] = BaseUrl;
                if (ApiKey is not null) env["OPENAI_API_KEY"] = ApiKey;
                break;
            case ProviderKind.Google:
                if (ApiKey is not null) env["GEMINI_API_KEY"] = ApiKey;
                break;
        }
        return env;
    }
}
