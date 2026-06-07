namespace Armadillo.Core.Persistence;

/// <summary>
/// "Save everything." Append-on-transition persistence for jobs, sessions, reviews, learnings,
/// capability snapshots, and an append-only audit trail. Backed by SQLite (WAL, single writer).
/// </summary>
public interface IStore : IDisposable
{
    void Initialize();

    void SaveJob(JobRecord job);
    void SaveSession(SessionRecord session);
    void SaveReview(ReviewRecord review);
    void SaveLearning(LearningRecord learning);

    /// <summary>Most-recent learnings for a persona (recency retrieval; cheap fallback to RAG).</summary>
    IReadOnlyList<LearningRecord> RecentLearnings(string persona, int k);

    /// <summary>All learnings that have an embedding, for brute-force cosine retrieval.</summary>
    IReadOnlyList<LearningRecord> LearningsWithEmbeddings(int max = 2000);

    void SaveCapabilitySnapshot(DateTimeOffset takenAt, string json);

    void Audit(string actor, string action, string target, string? detailJson = null);

    /// <summary>Most recent jobs (for dashboards/activity views).</summary>
    IReadOnlyList<JobRecord> RecentJobs(int n);

    /// <summary>Most recent sessions (for dashboards/activity views).</summary>
    IReadOnlyList<SessionRecord> RecentSessions(int n);

    /// <summary>Per-tool quality priors from past reviewed sessions (approve-weighted). Feeds learned routing.</summary>
    IReadOnlyList<ToolPrior> ToolPriors();

    // --- self-improvement (Phase 4) ---

    /// <summary>Insert a new playbook version. Set <c>active</c> separately via <see cref="SetActivePlaybook"/>.</summary>
    void SavePlaybook(PlaybookRecord playbook);
    PlaybookRecord? GetActivePlaybook(string name);
    IReadOnlyList<PlaybookRecord> GetPlaybookVersions(string name);
    void SetActivePlaybook(string name, int version);

    void SaveExperiment(ExperimentRecord experiment);

    /// <summary>Tiny key/value settings store (kill switch, autonomy level, …).</summary>
    void SetSetting(string key, string value);
    string? GetSetting(string key);
}
