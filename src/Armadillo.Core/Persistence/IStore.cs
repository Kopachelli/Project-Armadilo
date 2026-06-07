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
}
