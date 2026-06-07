using Armadillo.Core;
using Armadillo.Core.Adapters;
using Armadillo.Core.Brain;
using Armadillo.Core.Llm;
using Armadillo.Core.Orchestration;
using Armadillo.Core.Persistence;
using Armadillo.Core.Review;
using Armadillo.Core.Spawning;
using Armadillo.Core.Tools;
using Armadillo.Detection;

namespace Armadillo.Host;

public sealed record RegistratorOptions
{
    public string? Workspace { get; init; }
    public string? ReviewModel { get; init; }
    public string? EmbedModel { get; init; }
    public GovernorOptions Governor { get; init; } = new();
}

/// <summary>
/// Composition root: wires the whole spine (detection, spawning, persistence, brain, review,
/// governor, dispatcher) behind one object the Host and the MCP server both consume. Lives in the
/// Host because it instantiates the concrete <see cref="ToolDetector"/> from Armadillo.Detection.
/// </summary>
public sealed class Registrator : IDisposable
{
    public IPathProvider Paths { get; }
    public IStore Store { get; }
    public IToolDetector Detector { get; }
    public AgentDispatcher Dispatcher { get; }
    public IBrain Brain { get; }
    public McpEndpoint McpEndpoint { get; }

    private Registrator(IPathProvider paths, IStore store, IToolDetector detector,
        AgentDispatcher dispatcher, IBrain brain, McpEndpoint mcpEndpoint)
    {
        Paths = paths;
        Store = store;
        Detector = detector;
        Dispatcher = dispatcher;
        Brain = brain;
        McpEndpoint = mcpEndpoint;
    }

    public static Registrator Create(RegistratorOptions? options = null, Action<string>? log = null)
    {
        options ??= new RegistratorOptions();

        var paths = new PathProvider(options.Workspace);
        paths.EnsureWorkspace();

        var store = new SqliteStore(paths.DatabaseFile);
        store.Initialize();

        var detector = new ToolDetector(home: paths.HomeDirectory);
        var ollama = new OllamaClient();
        var reviewer = new OllamaReviewer(ollama, options.ReviewModel);
        var brain = new Brain(store, ollama, options.EmbedModel);
        var governor = new Governor(options.Governor);
        var runner = new ProcessRunner();
        var adapters = new AdapterRegistry(new IToolAdapter[] { new ClaudeAdapter() });
        var mcpEndpoint = new McpEndpoint();

        var dispatcher = new AgentDispatcher(adapters, detector, runner, store, brain, reviewer,
            governor, paths, mcpEndpoint, log);

        return new Registrator(paths, store, detector, dispatcher, brain, mcpEndpoint);
    }

    public void Dispose() => Store.Dispose();
}
