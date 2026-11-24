using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Traceability.Infrastructure.Foundation;

/// <summary>Konfigurasi database fondasi — seluruhnya dapat diset lewat environment.</summary>
public sealed class TraceDbOptions
{
    /// <summary>Satu-satunya provider yang didukung Fase 1; validasi menolak nilai lain.</summary>
    public string Provider { get; set; } = "sqlite";

    /// <summary>Path file DB (relatif terhadap content root bila tidak absolut).</summary>
    public string Path { get; set; } = "data/traceability.db";

    /// <summary>Bila true, semua endpoint operasi mewajibkan sesi login + izin.</summary>
    public bool AuthEnforced { get; set; } = true;

    public string? BootstrapAdminEmail { get; set; }
    public string? BootstrapAdminPassword { get; set; }
    public int SessionLifetimeHours { get; set; } = 12;

    /// <summary>Fase 2: batas terukur jumlah label per permintaan generate/job cetak.</summary>
    public int LabelMaxPerRequest { get; set; } = 500;

    /// <summary>Fase 3: batas percobaan rework per unit (default tiga, SSOT §5).</summary>
    public int NgMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Lokasi catatan penerbitan label (ledger). Default: berkas di samping DB, sehingga
    /// backup DB yang tertinggal tetap dapat direkonsiliasi terhadap ledger yang lebih baru.
    /// </summary>
    public string? LabelLedgerPath { get; set; }

    public string ResolvedPath(string contentRoot)
    {
        var raw = string.IsNullOrWhiteSpace(Path) ? "data/traceability.db" : Path.Trim();
        if (System.IO.Path.IsPathRooted(raw)) return raw;
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRoot, raw.Replace('/', System.IO.Path.DirectorySeparatorChar)));
    }

    /// <summary>Path ledger penerbitan label, relatif terhadap content root bila tidak absolut.</summary>
    public string ResolvedLedgerPath(string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(LabelLedgerPath))
        {
            var raw = LabelLedgerPath.Trim();
            return System.IO.Path.IsPathRooted(raw)
                ? raw
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRoot,
                    raw.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        }

        var directory = System.IO.Path.GetDirectoryName(ResolvedPath(contentRoot)) ?? contentRoot;
        return System.IO.Path.Combine(directory, "label-issuance.log");
    }
}

/// <summary>
/// Titik tunggal akses ke database relasional fondasi. Untuk pindah ke
/// PostgreSQL/MySQL, ganti pembuatan koneksi di sini — service tidak berubah.
/// </summary>
public sealed class TraceDb
{
    private readonly TraceDbOptions _options;
    private readonly string _databasePath;

    public TraceDb(TraceDbOptions options, string contentRoot)
    {
        if (!string.Equals(options.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Provider database \"{options.Provider}\" belum didukung; Fase 1 memakai sqlite.");
        }

        _options = options;
        _databasePath = options.ResolvedPath(contentRoot);
        var directory = System.IO.Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    public TraceDbOptions Options => _options;
    public string DatabasePath => _databasePath;

    public SqliteConnection OpenConnection()
    {
        var settings = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            DefaultTimeout = 5,
            ForeignKeys = true,
        };
        var connection = new SqliteConnection(settings.ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Jalankan migrasi terversi. Aman dipanggil berulang (idempoten).</summary>
    public IReadOnlyList<string> Initialize()
    {
        using var connection = OpenConnection();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        using (var ensure = connection.CreateCommand())
        {
            ensure.CommandText =
                "CREATE TABLE IF NOT EXISTS schema_versions (" +
                " version TEXT PRIMARY KEY," +
                " applied_at_utc TEXT NOT NULL);";
            ensure.ExecuteNonQuery();
        }

        return MigrationRunner.ApplyAll(connection);
    }
}

/// <summary>
/// Menerapkan skrip SQL tertanam sesuai urutan nama. Versi yang sudah tercatat
/// di schema_versions dilewati, sehingga migrasi dapat dijalankan ulang tanpa
/// seed ganda.
/// </summary>
internal static class MigrationRunner
{
    private const string ResourceFolder = "Migrations";

    public static IReadOnlyList<string> ApplyAll(SqliteConnection connection)
    {
        var applied = new List<string>();
        var scripts = DiscoverScripts();

        foreach (var (version, sql) in scripts)
        {
            if (IsApplied(connection, version)) continue;

            using var transaction = connection.BeginTransaction();
            using (var run = connection.CreateCommand())
            {
                run.Transaction = transaction;
                run.CommandText = sql;
                run.ExecuteNonQuery();
            }

            using (var log = connection.CreateCommand())
            {
                log.Transaction = transaction;
                log.CommandText =
                    "INSERT INTO schema_versions (version, applied_at_utc) VALUES ($version, $appliedAt);";
                log.Parameters.AddWithValue("$version", version);
                log.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
                log.ExecuteNonQuery();
            }

            transaction.Commit();
            applied.Add(version);
        }

        return applied;
    }

    private static bool IsApplied(SqliteConnection connection, string version)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM schema_versions WHERE version = $version;";
        check.Parameters.AddWithValue("$version", version);
        return Convert.ToInt64(check.ExecuteScalar()) > 0;
    }

    private static IEnumerable<(string Version, string Sql)> DiscoverScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var results = new List<(string, string)>();

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains($".{ResourceFolder}.", StringComparison.Ordinal)
                                    && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // Nama resource: Traceability.Infrastructure.Migrations.0001_foundation.sql
            var scriptName = resource.Split('.')[^2];
            var version = scriptName.Split('_')[0];
            results.Add((version, reader.ReadToEnd()));
        }

        return results;
    }
}
