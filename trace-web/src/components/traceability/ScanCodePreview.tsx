'use client';

import { useEffect, useState } from 'react';

export function ScanCodePreview({ value }: { value: string }) {
  const [code, setCode] = useState('');
  const [image, setImage] = useState<{ code: string; url: string } | null>(null);
  const [failed, setFailed] = useState('');

  useEffect(() => {
    const entered = value.trim();
    if (entered) setCode(entered);
  }, [value]);

  useEffect(() => {
    if (!code) return;
    const controller = new AbortController();
    let url: string | undefined;
    const timer = window.setTimeout(async () => {
      try {
        const response = await fetch('/api/v1/ng/scan-preview', {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ value: code }),
          signal: controller.signal,
        });
        if (!response.ok || !response.headers.get('content-type')?.startsWith('image/png')) {
          throw new Error('Pratinjau QR gagal.');
        }
        const blob = await response.blob();
        if (controller.signal.aborted) return;
        url = URL.createObjectURL(blob);
        setFailed('');
        setImage({ code, url });
      } catch {
        if (!controller.signal.aborted) setFailed(code);
      }
    }, 180);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
      if (url) URL.revokeObjectURL(url);
    };
  }, [code]);

  return (
    <figure className="scan-code-preview" aria-label="Hasil scan">
      <div className="scan-code-preview-image">
        {image?.code === code ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img key={image.url} src={image.url} width={128} height={128} alt="QR hasil scan" />
        ) : (
          <span>{!code ? 'Belum ada scan' : failed === code ? 'QR tidak tersedia' : 'Memuat QR…'}</span>
        )}
      </div>
      {code && <figcaption className="mono">{code}</figcaption>}
    </figure>
  );
}
