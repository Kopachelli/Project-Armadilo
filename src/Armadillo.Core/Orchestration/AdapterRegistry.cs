using Armadillo.Core.Adapters;
using Armadillo.Core.Tools;

namespace Armadillo.Core.Orchestration;

/// <summary>Resolves the <see cref="IToolAdapter"/> for a tool. Register one per supported CLI.</summary>
public sealed class AdapterRegistry
{
    private readonly Dictionary<ToolId, IToolAdapter> _adapters;

    public AdapterRegistry(IEnumerable<IToolAdapter> adapters)
        => _adapters = adapters.ToDictionary(a => a.Id);

    public bool Supports(ToolId id) => _adapters.ContainsKey(id);

    public IToolAdapter Get(ToolId id) =>
        _adapters.TryGetValue(id, out var a)
            ? a
            : throw new NotSupportedException($"No adapter registered for {id} yet.");

    public IReadOnlyCollection<ToolId> SupportedTools => _adapters.Keys;
}
