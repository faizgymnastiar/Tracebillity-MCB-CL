using QRCoder;
using Traceability.Application;

namespace Traceability.Infrastructure.Foundation;

public sealed class UsbPrintService(TraceDb db, LabelStore labels)
{
    public object Status(string jobId)
    {
        using var connection = db.OpenConnection();
        return Sql.ReadOne(connection, """
            SELECT state,claimed_at,reported_at FROM usb_print_dispatch WHERE job_id=$job;
            """, r => new { State = r.GetString(0), ClaimedAt = r.GetString(1),
                ReportedAt = Sql.NullableText(r, "reported_at") }, ("$job", jobId))
            ?? (object)new { State = "PENDING" };
    }

    public object Claim(string jobId, string actor)
    {
        var job = labels.FindJob(jobId);
        if (job is null || job.Channel != "DIRECT_PRINTER")
            throw FoundationErrors.NotFound("Job cetak USB tidak ditemukan.");

        using var generator = new QRCodeGenerator();
        var pages = labels.JobLabels(jobId).Select(label =>
        {
            using var qr = generator.CreateQrCode(label.Payload, QRCodeGenerator.ECCLevel.M);
            return new { label.Id, Rows = qr.ModuleMatrix.Select(row =>
                string.Concat(row.Cast<bool>().Select(dark => dark ? '1' : '0'))).ToArray() };
        }).ToArray();
        if (pages.Length != job.Quantity)
            throw FoundationErrors.Conflict("USB_JOB_INVALID", "Isi job cetak tidak lengkap.");

        using var connection = db.OpenConnection();
        var inserted = Sql.ExecuteCount(connection, """
            INSERT INTO usb_print_dispatch (job_id,actor_id,state,claimed_at)
            SELECT id,$actor,'CLAIMED',$now FROM print_jobs
            WHERE id=$job AND channel='DIRECT_PRINTER' AND status='EXPORTED'
            ON CONFLICT(job_id) DO NOTHING;
            """, ("$job", jobId), ("$actor", actor), ("$now", DateTimeOffset.UtcNow.ToString("O")));
        if (inserted != 1)
            throw FoundationErrors.Conflict("USB_ALREADY_DISPATCHED", "Job sudah diambil atau ditutup. Periksa hasil sebelum mencetak ulang.");
        return new { JobId = jobId, Pages = pages };
    }

    public object Report(string jobId, string actor, string state)
    {
        if (state is not ("SPOOLED" or "FAILED" or "UNKNOWN"))
            throw FoundationErrors.BadRequest("USB_STATE", "Status cetak USB tidak valid.");
        using var connection = db.OpenConnection();
        var updated = Sql.ExecuteCount(connection, """
            UPDATE usb_print_dispatch SET state=$state,reported_at=$now
            WHERE job_id=$job AND actor_id=$actor AND state='CLAIMED';
            """, ("$state", state), ("$now", DateTimeOffset.UtcNow.ToString("O")),
            ("$job", jobId), ("$actor", actor));
        if (updated != 1 && Sql.ScalarLong(connection, """
            SELECT COUNT(*) FROM usb_print_dispatch WHERE job_id=$job AND actor_id=$actor AND state=$state;
            """, ("$job", jobId), ("$actor", actor), ("$state", state)) == 0)
            throw FoundationErrors.Conflict("USB_NOT_CLAIMED", "Job belum diambil oleh akun ini.");
        return Status(jobId);
    }
}
