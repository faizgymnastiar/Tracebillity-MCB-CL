using System.Globalization;

namespace Traceability.Infrastructure.Foundation;

/// <summary>
/// Catatan penerbitan label yang disimpan DI LUAR file database (append-only).
/// Fungsinya hanya satu: menjadi lantai (floor) batas tertinggi digit yang pernah
/// diterbitkan, sehingga restore backup yang tertinggal tidak dapat menerbitkan
/// ulang ID yang mungkin masih beredar di lantai produksi (Fase 2 langkah 6).
///
/// Baris:  {waktuUtc}|{batchId}|{firstSeq}|{lastSeq}|{pelaku}
/// Baris rekonsiliasi memakai batchId "RECONCILE".
/// </summary>
public sealed class LabelIssuanceLedger
{
    private readonly string _path;
    private readonly object _writeGate = new();

    public LabelIssuanceLedger(string path)
    {
        _path = path;
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    public string Path => _path;
    public bool Exists => File.Exists(_path);

    /// <summary>Batas tertinggi yang tercatat. Null + pesan error bila berkas tidak terbaca.</summary>
    public long? TryReadHighWater(out string? error)
    {
        error = null;
        if (!Exists) return null;

        try
        {
            var max = 0L;
            foreach (var line in File.ReadLines(_path))
            {
                if (TryReadLastSeq(line, out var lastSeq) && lastSeq > max) max = lastSeq;
            }

            return max;
        }
        catch (IOException exception)
        {
            error = $"Catatan penerbitan {System.IO.Path.GetFileName(_path)} tidak dapat dibaca: {exception.Message}";
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"Catatan penerbitan {System.IO.Path.GetFileName(_path)} tidak dapat dibaca: {exception.Message}";
            return null;
        }
    }

    /// <summary>Tambah satu baris penerbitan. Melempar bila berkas gagal ditulis.</summary>
    public void Append(string batchId, long firstSeq, long lastSeq, string actor)
    {
        var line = string.Join('|',
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            batchId,
            firstSeq.ToString(CultureInfo.InvariantCulture),
            lastSeq.ToString(CultureInfo.InvariantCulture),
            Sanitize(actor));

        lock (_writeGate)
        {
            File.AppendAllText(_path, line + Environment.NewLine, System.Text.Encoding.UTF8);
        }
    }

    public void AppendReconciliation(long highWaterSeq, string actor, string note)
    {
        Append("RECONCILE", 0, highWaterSeq, string.IsNullOrWhiteSpace(note) ? actor : $"{actor} — {note}");
    }

    private static bool TryReadLastSeq(string line, out long lastSeq)
    {
        lastSeq = 0;
        var parts = line.Split('|');
        // Format baru: waktu|batch|first|last|aktor ; baris rusak dilewati (hanya floor).
        if (parts.Length < 4) return false;
        return long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out lastSeq);
    }

    private static string Sanitize(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        // Kolom ke-2 dipakai sebagai penanda jenis baris, jadi kata kuncinya tidak boleh
        // muncul di nama pelaku; pemisah kolom dan ganti baris dinetralkan.
        if (text.Contains("RECONCILE", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Replace("RECONCILE", "rekonsiliasi", StringComparison.OrdinalIgnoreCase);
        }

        return text.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}
