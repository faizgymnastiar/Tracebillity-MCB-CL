'use client';

import { useEffect, useState } from 'react';
import { getClockWib, getLongDateWib } from '@/lib/datetime';

interface LiveClockProps {
  compact?: boolean;
}

export function LiveClock({ compact = false }: LiveClockProps) {
  const [now, setNow] = useState<Date | null>(null);

  useEffect(() => {
    setNow(new Date());
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  if (!now) {
    return <span className="helper">{compact ? '--:--:-- WIB' : 'Memuat waktu WIB…'}</span>;
  }

  return (
    <span className="helper">
      {compact ? `${getClockWib(now)} WIB` : `${getLongDateWib(now)} • ${getClockWib(now)} WIB`}
    </span>
  );
}
