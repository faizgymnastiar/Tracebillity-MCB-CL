interface ApiOfflineNoticeProps {
  origin: string;
}

export function ApiOfflineNotice({ origin }: ApiOfflineNoticeProps) {
  return (
    <section className="offline-card">
      <div className="eyebrow">STATUS SISTEM</div>
      <h1>Trace API tidak dapat dihubungi</h1>
      <p className="lead">
        Halaman Next.js berjalan normal, tetapi backend <code>trace-api</code> (.NET) di{' '}
        <code>{origin}</code> belum menerima koneksi, jadi data scan, database trace, dan laporan tidak bisa
        dimuat.
      </p>

      <ol className="offline-steps">
        <li>
          Jalankan API dari folder repo:
          <div>
            <code>dotnet run --project trace-api/src/Traceability.Api/Traceability.Api.csproj</code>
          </div>
        </li>
        <li>
          Jika muncul <em>An Application Control policy has blocked this file (0x800711C7)</em>, Windows Smart
          App Control sedang memblokir DLL hasil build lokal. Matikan lewat Windows Security &rarr; App &amp;
          browser control &rarr; Smart App Control &rarr; Off, atau jalankan API di container:
          <div>
            <code>docker compose -f deployment/compose.yaml up --build</code>
          </div>
        </li>
        <li>
          Pastikan health check mengembalikan <code>200</code> pada <code>{origin}/api/v1/health</code>, lalu
          muat ulang halaman ini.
        </li>
      </ol>
    </section>
  );
}
