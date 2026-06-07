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

            CREATE TABLE IF NOT EXISTS playbooks (
              name TEXT NOT NULL, version INTEGER NOT NULL, text TEXT NOT NULL, score REAL,
              active INTEGER NOT NULL DEFAULT 0, source_note TEXT, created_at TEXT NOT NULL,
              PRIMARY KEY(name, version));

            CREATE TABLE IF NOT EXISTS experiments (
              experiment_id TEXT PRIMARY KEY, playbook_name TEXT, incumbent_version INTEGER,
              candidate_version INTEGER, incumbent_score REAL, candidate_score REAL, sample_size INTEGER,
              outcome TEXT, detail TEXT, at TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
    }

    public void SavePlaybook(PlaybookRecord p) => Write(cmd =>
    {
        cmd.CommandText = """
            INSERT INTO playbooks (name,version,text,score,active,source_note,created_at)
            VALUES ($n,$v,$t,$s,$a,$src,$created)
            ON CONFLICT(name,version) DO UPDATE SET text=$t, score=$s, active=$a, source_note=$src;
            """;
        cmd.P("$n", p.Name); cmd.P("$v", p.Version); cmd.P("$t", p.Text); cmd.P("$s", p.Score);
        cmd.P("$a", p.Active ? 1 : 0); cmd.P("$src", p.SourceNote); cmd.P("$created", Iso(p.CreatedAt));
    });

    public PlaybookRecord? GetActivePlaybook(string name)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM playbooks WHERE name=$n AND active=1 LIMIT 1;";
            cmd.P("$n", name);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadPlaybook(r) : null;
        }
    }

    public IReadOnlyList<PlaybookRecord> GetPlaybookVersions(string name)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM playbooks WHERE name=$n ORDER BY version;";
            cmd.P("$n", name);
            var list = new List<PlaybookRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadPlaybook(r));
            return list;
        }
    }

    public void SetActivePlaybook(string name, int version) => Write(cmd =>
    {
        cmd.CommandText = "UPDATE playbooks SET active=0 WHERE name=$n; " +
                          "UPDATE playbooks SET active=1 WHERE name=$n AND version=$v;";
        cmd.P("$n", name); cmd.P("$v", version);
    });

    public void SaveExperiment(ExperimentRecord e) => Write(cmd =>
    {
        cmd.CommandText = """
            INSERT INTO experiments (experiment_id,playbook_name,incumbent_version,candidate_version,
              incumbent_score,candidate_score,sample_size,outcome,detail,at)
            VALUES ($id,$n,$iv,$cv,$is,$cs,$ss,$o,$d,$at);
            """;
        cmd.P("$id", e.ExperimentId); cmd.P("$n", e.PlaybookName); cmd.P("$iv", e.IncumbentVersion);
        cmd.P("$cv", e.CandidateVersion); cmd.P("$is", e.IncumbentScore); cmd.P("$cs", e.CandidateScore);
        cmd.P("$ss", e.SampleSize); cmd.P("$o", e.Outcome); cmd.P("$d", e.Detail); cmd.P("$at", Iso(e.At));
    });

    public void SetSetting(string key, string value) => Write(cmd =>
    {
        cmd.CommandText = "INSERT INTO settings (key,value) VALUES ($k,$v) " +
                          "ON CONFLICT(key) DO UPDATE SET value=$v;";
        cmd.P("$k", key); cmd.P("$v", value);
    });

    public string? GetSetting(string key)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key=$k;";
            cmd.P("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    private static PlaybookRecord ReadPlaybook(SqliteDataReader r) => new()
    {
        Name = (string)r["name"],
        Version = Convert.ToInt32(r["version"]),
        Text = (string)r["text"],
        Score = r["score"] is double d ? d : 0,
        Active = Convert.ToInt64(r["active"]) == 1,
        SourceNote = r["source_note"] as string,
        CreatedAt = DateTimeOffset.Parse((string)r["created_at"]),
    };

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

    public IReadOnlyList<JobRecord> RecentJobs(int n)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM jobs ORDER BY created_at DESC LIMIT $n;";
            cmd.P("$n", n);
            var list = new List<JobRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new JobRecord
                {
                    JobId = (string)r["job_id"],
                    ParentJobId = r["parent_job_id"] as string,
                    RequesterSession = r["requester_session"] as string,
                    RootId = (string)r["root_id"],
                    Depth = Convert.ToInt32(r["depth"]),
                    Persona = (string)r["persona"],
                    Task = (string)r["task"],
                    Tool = Enum.TryParse<ToolId>((string)r["tool"], out var t) ? t : ToolId.Claude,
                    State = Enum.TryParse<JobState>((string)r["state"], out var s) ? s : JobState.Received,
                    CreatedAt = DateTimeOffset.Parse((string)r["created_at"]),
                    UpdatedAt = DateTimeOffset.Parse((string)r["updated_at"]),
                });
            return list;
        }
    }

    public IReadOnlyList<SessionRecord> RecentSessions(int n)
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions ORDER BY started_at DESC LIMIT $n;";
            cmd.P("$n", n);
            var list = new List<SessionRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SessionRecord
                {
                    SessionId = (string)r["session_id"],
                    JobId = (string)r["job_id"],
                    Tool = Enum.TryParse<ToolId>((string)r["tool"], out var t) ? t : ToolId.Claude,
                    Model = r["model"] as string,
                    ProviderSessionId = r["provider_session_id"] as string,
                    ExitReason = r["exit_reason"] as string ?? "",
                    InputTokens = r["input_tokens"] is long it ? it : 0,
                    OutputTokens = r["output_tokens"] is long ot ? ot : 0,
                    CostUsd = r["cost_usd"] is double c ? c : 0,
                    TranscriptPath = r["transcript_path"] as string,
                    StartedAt = DateTimeOffset.TryParse(r["started_at"] as string, out var sa) ? sa : default,
                    EndedAt = DateTimeOffset.TryParse(r["ended_at"] as string, out var ea) ? ea : default,
                });
            return list;
        }
    }

    public IReadOnlyList<ToolPrior> ToolPriors()
    {
        lock (_writeLock)
        {
            using var cmd = _conn.CreateCommand();
            // Approve-weighted quality score per tool over reviewed sessions.
            cmd.CommandText = """
                SELECT s.tool AS tool,
                       AVG(CASE r.verdict WHEN 'approve' THEN r.confidence
                                          WHEN 'revise'  THEN r.confidence * 0.5
                                          ELSE 0 END) AS score,
                       COUNT(*) AS n
                FROM sessions s JOIN reviews r ON r.session_id = s.session_id
                WHERE r.verdict <> 'skipped'
                GROUP BY s.tool;
                """;
            var list = new List<ToolPrior>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!Enum.TryParse<ToolId>((string)r["tool"], out var tool)) continue;
                list.Add(new ToolPrior(tool, r["score"] is double d ? d : 0, Convert.ToInt32(r["n"])));
            }
            return list;
        }
    }

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
