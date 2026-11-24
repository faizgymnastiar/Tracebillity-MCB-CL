'use client';

import { useState } from 'react';

export function PrintLabelButton({ expectedCount }: { expectedCount: number }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function printLabels() {
    setBusy(true);
    setError('');
    try {
      const images = [...document.querySelectorAll<HTMLImageElement>('.ng-label-print img')];
      if (images.length !== expectedCount) throw new Error('Jumlah QR tidak sesuai.');
      await Promise.all(images.map((image) => image.decode()));
      window.print();
    } catch {
      setError('Ada QR yang belum berhasil dimuat. Muat ulang pratinjau sebelum mencetak.');
    } finally {
      setBusy(false);
    }
  }

  return <>
    <button type="button" className="btn-primary" disabled={busy} onClick={() => void printLabels()}>
      {busy ? 'Menyiapkan QR…' : `Cetak ${expectedCount} label`}
    </button>
    {error && <p role="alert">{error}</p>}
  </>;
}
