using Armadillo.Core.Tools;
using Microsoft.Data.Sqlite;

namespace Armadillo.Core.Persistence;

/// <summary>SQLite-backed <see cref="IStore"/>. WAL mode, a single serialized writer connection.</summary>
public sealed class SqliteStore : IStore
{
    private readonly SqliteConnection _conn;
    private readonly object _writeLock = new();

    public SqliteStore(string databaseFile)
    {
        var dir = Path.GetDirectoryName(databaseFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={databaseFile};Cache=Shared");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;");
    }

    public void Initialize()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS jobs (
              job_id TEXT PRIMARY KEY, parent_job_id TEXT, requester_session TEXT,
              root_id TEXT NOT NULL, depth INTEGER NOT NULL, persona TEXT NOT NULL,
              task TEXT NOT NULL, tool TEXT NOT NULL, state TEXT NOT NULL,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS sessions (
              session_id TEXT PRIMARY KEY, job_id TEXT NOT NULL, tool TEXT NOT NULL,
              model TEXT, provider_session_id TEXT, exit_reason TEXT,
              input_tokens INTEGER, output_tokens INTEGER, cost_usd REAL,
              transcript_path TEXT, started_at TEXT, ended_at TEXT);

            CREATE TABLE IF NOT EXISTS reviews (
              review_id TEXT PRIMARY KEY, session_id TEXT NOT NULL, reviewer_model TEXT,
              verdict TEXT, confidence REAL, rationale TEXT, at TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS learnings (
              learning_id TEXT PRIMARY KEY, persona TEXT NOT NULL, tool TEXT NOT NULL,
              text TEXT NOT NULL, confidence REAL, source_job TEXT, embedding BLOB,
              created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_learnings_persona ON learnings(persona, created_at DESC);

            CREATE TABLE IF NOT EXISTS capability_snaps (
              snap_id INTEGER PRIMARY KEY AUTOINCREMENT, taken_at TEXT NOT NULL, json TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS audit (
              audit_id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, actor TEXT,
              action TEXT, target TEXT, detail_json TEXT);
            """);
    }

    public void SaveJob(JobRecord j) => Write(cmd =>
    {
        cmd.CommandText = """
            INSERT INTO jobs (job_id,parent_job_id,requester_session,root_id,depth,persona,task,tool,state,created_at,updated_at)
            VALUES ($id,$parent,$req,$root,$depth,$persona,$task,$tool,$state,$created,$updated)
            ON CONFLICT(job_id) DO UPDATE SET state=$state, updated_at=$updated;
            """;
        cmd.P("$id", j.JobId); cmd.P("$parent", j.ParentJobId); cmd.P("$req", j.RequesterSession);
        cmd.P("$root", j.RootId); cmd.P("$depth", j.Depth); cmd.P("$persona", j.Persona);
        cmd.P("$task", j.Task); cmd.P("$tool", j.Tool.ToString()); cmd.P("$state", j.State.ToString());
        cmd.P("$created", Iso(j.CreatedAt)); cmd.P("$updated", Iso(j.UpdatedAt));
    });

    public void SaveSession(SessionRecord s) => Write(cmd =>
    {
        cmd.CommandText = """
            INSERT INTO sessions (session_id,job_id,tool,model,provider_session_id,exit_reason,input_tokens,output_tokens,cost_usd,transcript_path,started_at,ended_at)
            VALUES ($id,$job,$tool,$model,$psid,$exit,$in,$out,$cost,$tp,$start,$end)
            ON CONFLICT(session_id) DO UPDATE SET exit_reason=$exit,input_tokens=$in,output_tokens=$out,cost_usd=$cost,transcript_path=$tp,ended_at=$end;
            """;
        cmd.P("$id", s.SessionId); cmd.P("$job", s.JobId); cmd.P("$tool", s.Tool.ToString());
        cmd.P("$model", s.Model); cmd.P("$psid", s.ProviderSessionId); cmd.P("$exit", s.ExitReason);
        cmd.P("$in", s.InputTokens); cmd.P("$out", s.OutputTokens); cmd.P("$cost", s.CostUsd);
        cmd.P("$tp", s.TranscriptPath); cmd.P("$start", Iso(s.StartedAt)); cmd.P("$end", Iso(s.EndedAt));
    });

    public void SaveReview(ReviewRecord r) => Write(cmd =>
    {
        cmd.CommandText = """
            INSERT INTO reviews (review_id,session_id,reviewer_model,verdict,confidence,rationale,at)
            VALUES ($id,$sid,$model,$verdict,$conf,$rat,$at);
            """;
        cmd.P("$id", r.ReviewId); cmd.P("$sid", r.SessionId); cmd.P("$model", r.ReviewerModel);
        cmd.P("$verdict", r.Verdict); cmd.P("$conf", r.Confidence); cmd.P("$rat", r.Rationale);
        cmd.P("$at", Iso(r.At));
    });

    public void SaveLearning(LearningRecord l) => Write(cmd =>
    {
        cmd.CommandText = """
            INSERT INTO learnings (learning_id,persona,tool,text,confidence,source_job,embedding,created_at)
            VALUES ($id,$persona,$tool,$text,$conf,$src,$emb,$created);
            """;
        cmd.P("$id", l.LearningId); cmd.P("$persona", l.Persona); cmd.P("$tool", l.Tool.ToString());
        cmd.P("$text", l.Text); cmd.P("$conf", l.Confidence); cmd.P("$src", l.SourceJob);
        cmd.P("$emb", l.Embedding is null ? (object)DBNull.Value : Floats.ToBytes(l.Embedding));
        cmd.P("$created", Iso(l.CreatedAt));
    });

    public IReadOnlyList<LearningRecord> RecentLearnings(string persona, int k)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM learnings WHERE persona=$p ORDER BY created_at DESC LIMIT $k;";
            cmd.P("$p", persona); cmd.P("$k", k);
            return ReadLearnings(cmd);
        }
    }

    public IReadOnlyList<LearningRecord> LearningsWithEmbeddings(int max = 2000)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM learnings WHERE embedding IS NOT NULL ORDER BY created_at DESC LIMIT $k;";
            cmd.P("$k", max);
            return ReadLearnings(cmd);
        }
    }

    public void SaveCapabilitySnapshot(DateTimeOffset takenAt, string json) => Write(cmd =>
    {
        cmd.CommandText = "INSERT INTO capability_snaps (taken_at, json) VALUES ($t,$j);";
        cmd.P("$t", Iso(takenAt)); cmd.P("$j", json);
    });

    public void Audit(string actor, string action, string target, string? detailJson = null) => Write(cmd =>
    {
        cmd.CommandText = "INSERT INTO audit (at,actor,action,target,detail_json) VALUES ($at,$actor,$action,$target,$d);";
        cmd.P("$at", Iso(DateTimeOffset.UtcNow)); cmd.P("$actor", actor); cmd.P("$action", action);
        cmd.P("$target", target); cmd.P("$d", detailJson);
    });

    private static IReadOnlyList<LearningRecord> ReadLearnings(SqliteCommand cmd)
    {
        var list = new List<LearningRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            float[]? emb = null;
            var embOrd = r.GetOrdinal("embedding");
            if (!r.IsDBNull(embOrd)) emb = Floats.FromBytes((byte[])r["embedding"]);
            list.Add(new LearningRecord
            {
                LearningId = (string)r["learning_id"],
                Persona = (string)r["persona"],
                Tool = Enum.TryParse<ToolId>((string)r["tool"], out var t) ? t : ToolId.Claude,
                Text = (string)r["text"],
                Confidence = r["confidence"] is double d ? d : 0,
                SourceJob = r["source_job"] as string,
                Embedding = emb,
                CreatedAt = DateTimeOffset.Parse((string)r["created_at"]),
            });
        }
        return list;
    }

    private void Write(Action<SqliteCommand> build)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            build(cmd);
            cmd.ExecuteNonQuery();
        }
    }

    private void Exec(string sql)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static string Iso(DateTimeOffset t) => t.ToString("o");

    public void Dispose() => _conn.Dispose();
}

internal static class SqliteCommandExtensions
{
    public static void P(this SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
}

internal static class Floats
{
    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] b)
    {
        var v = new float[b.Length / sizeof(float)];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }
}
