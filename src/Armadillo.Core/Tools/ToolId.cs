namespace Armadillo.Core.Tools;

/// <summary>
/// The AI CLI tools and local-model runtimes the harness can detect and drive.
/// The string name (enum identity) is the persisted key used in the DB and logs — keep stable.
/// </summary>
public enum ToolId
{
    Claude,
    Codex,
    Cursor,
    Gemini,
    Qwen,
    Copilot,
    Pi,
    OpenCode,
    Zai,
    Ollama,
}
