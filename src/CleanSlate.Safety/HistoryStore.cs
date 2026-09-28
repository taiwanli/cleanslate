using Microsoft.Data.Sqlite;

namespace CleanSlate.Safety;

/// <summary>
/// SQLite-backed history store for cleanup jobs and software snapshots.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public HistoryStore(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate",
            "data",
            "history.db");

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        _connection.Open();
        EnsureSchema();
    }

    public string DbPath => _dbPath;

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS cleanup_jobs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                mode TEXT NOT NULL,
                target_name TEXT,
                utc TEXT NOT NULL,
                succeeded INTEGER NOT NULL DEFAULT 0,
                skipped INTEGER NOT NULL DEFAULT 0,
                failed INTEGER NOT NULL DEFAULT 0,
                report_path TEXT
            );
            CREATE TABLE IF NOT EXISTS cleanup_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                kind TEXT,
                path_or_key TEXT,
                risk TEXT,
                outcome TEXT,
                message TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_jobs_job ON cleanup_jobs(job_id);
            CREATE INDEX IF NOT EXISTS ix_items_job ON cleanup_items(job_id);
            """;
        cmd.ExecuteNonQuery();
    }

    public void InsertJob(string jobId, string mode, string? targetName, int succeeded, int skipped, int failed, string? reportPath)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO cleanup_jobs (job_id, mode, target_name, utc, succeeded, skipped, failed, report_path)
                VALUES ($job, $mode, $target, $utc, $ok, $skip, $fail, $report);
                """;
            cmd.Parameters.AddWithValue("$job", jobId);
            cmd.Parameters.AddWithValue("$mode", mode);
            cmd.Parameters.AddWithValue("$target", (object?)targetName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$ok", succeeded);
            cmd.Parameters.AddWithValue("$skip", skipped);
            cmd.Parameters.AddWithValue("$fail", failed);
            cmd.Parameters.AddWithValue("$report", (object?)reportPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertItem(string jobId, string kind, string pathOrKey, string risk, string outcome, string? message)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO cleanup_items (job_id, kind, path_or_key, risk, outcome, message)
                VALUES ($job, $kind, $path, $risk, $outcome, $msg);
                """;
            cmd.Parameters.AddWithValue("$job", jobId);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$path", pathOrKey);
            cmd.Parameters.AddWithValue("$risk", risk);
            cmd.Parameters.AddWithValue("$outcome", outcome);
            cmd.Parameters.AddWithValue("$msg", (object?)message ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<HistoryJobRow> ListJobs(int limit = 50)
    {
        lock (_gate)
        {
            var rows = new List<HistoryJobRow>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT job_id, mode, target_name, utc, succeeded, skipped, failed, report_path
                FROM cleanup_jobs
                ORDER BY id DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new HistoryJobRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }

            return rows;
        }
    }

    public IReadOnlyList<HistoryItemRow> ListItems(string jobId)
    {
        lock (_gate)
        {
            var rows = new List<HistoryItemRow>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT kind, path_or_key, risk, outcome, message
                FROM cleanup_items
                WHERE job_id = $job
                ORDER BY id;
                """;
            cmd.Parameters.AddWithValue("$job", jobId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new HistoryItemRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return rows;
        }
    }

    public void Dispose() => _connection.Dispose();
}

public sealed record HistoryJobRow(
    string JobId,
    string Mode,
    string? TargetName,
    string Utc,
    int Succeeded,
    int Skipped,
    int Failed,
    string? ReportPath);

public sealed record HistoryItemRow(
    string Kind,
    string PathOrKey,
    string Risk,
    string Outcome,
    string? Message);
