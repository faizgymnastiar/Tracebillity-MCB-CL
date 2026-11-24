import Link from 'next/link';

export default function NotFound() {
  return (
    <main style={{ maxWidth: 620, margin: '0 auto', padding: '72px 24px' }}>
      <div className="eyebrow">ERROR 404</div>
      <h1 style={{ fontSize: 26, margin: '8px 0 10px' }}>Halaman tidak ditemukan</h1>
      <p className="helper" style={{ fontSize: 13, marginBottom: 22 }}>
        Rute yang Anda buka tidak tersedia pada Traceability MCB-CL. Gunakan navigasi berikut untuk kembali ke gerbang
        yang benar.
      </p>
      <div className="form-actions">
        <Link href="/" className="hero-button primary">
          Control Room
        </Link>
        <Link href="/modules/traceability/database" className="hero-button ghost"
          style={{ borderColor: '#cbd5e1', color: '#161b41', padding: '10px 16px' }}>
          Database Trace
        </Link>
        <Link href="/login" className="hero-button ghost"
          style={{ borderColor: '#cbd5e1', color: '#161b41', padding: '10px 16px' }}>
          Login Operator
        </Link>
      </div>
    </main>
  );
}
