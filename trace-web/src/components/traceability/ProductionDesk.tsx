'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import type { MasterCatalog, ProductionContext, WorkOrderInfo } from '@/types';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/formatters';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface WoDraft {
  key: number;
  number: string;
  client: string;
  shippingReference: string;
  targetQty: string;
}

const FLOOR_HAK = ['scan.unit', 'scan.inner', 'scan.outer', 'ng.record'];

function emptyDraft(key: number): WoDraft {
  return { key, number: '', client: '', shippingReference: '', targetQty: '' };
}

function toInput(draft: WoDraft) {
  return {
    number: draft.number.trim().toUpperCase(),
    client: draft.client.trim(),
    shippingReference: draft.shippingReference.trim(),
    targetQty: Number.parseInt(draft.targetQty, 10) || 0,
  };
}

async function readError(response: Response, fallback: string): Promise<string> {
  try {
    const payload = (await response.json()) as { error?: string };
    return payload.error ?? fallback;
  } catch {
    return fallback;
  }
}

/**
 * Beranda operator Fase 1: konteks produksi aktif, modal "Mulai produksi"
 * (shift/line/SKU + multi-WO + pilih WO aktif), ganti WO aktif, dan akhiri sesi.
 */
export function ProductionDesk() {
  const [production, setProduction] = useState<ProductionContext | null>(null);
  const [catalog, setCatalog] = useState<MasterCatalog | null>(null);
  const [hak, setHak] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const [modalOpen, setModalOpen] = useState(false);
  const [shiftId, setShiftId] = useState('');
  const [lineId, setLineId] = useState('');
  const [skuId, setSkuId] = useState('');
  const [drafts, setDrafts] = useState<WoDraft[]>([emptyDraft(0)]);
  const [activeKey, setActiveKey] = useState(0);
  const [addWoOpen, setAddWoOpen] = useState(false);
  const [addDraft, setAddDraft] = useState<WoDraft>(emptyDraft(0));

  const reload = useCallback(async (): Promise<void> => {
    try {
      const [meResponse, currentResponse] = await Promise.all([
        fetch('/api/v1/auth/me', { cache: 'no-store' }),
        fetch('/api/v1/production/current', { cache: 'no-store' }),
      ]);
      if (meResponse.ok) {
        const me = (await meResponse.json()) as { hak?: string[]; production?: ProductionContext | null };
        setHak(me.hak ?? []);
        if (me.production) setProduction(me.production);
      }
      if (currentResponse.ok) {
        const current = (await currentResponse.json()) as { active: boolean; production: ProductionContext | null };
        setProduction(current.production);
      }
    } catch {
      setError('Gagal memuat konteks produksi.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
    fetch('/api/v1/master/catalog/active', { cache: 'no-store' })
      .then(async (response) => {
        if (response.ok) setCatalog((await response.json()) as MasterCatalog);
      })
      .catch(() => undefined);
  }, [reload]);

  const canOperate = useMemo(() => hak.some((item) => FLOOR_HAK.includes(item)), [hak]);
  const shiftChoices = catalog?.shifts ?? [];
  const lineChoices = catalog?.lines ?? [];
  const skuChoices = catalog?.skus ?? [];

  const openModal = (): void => {
    setError('');
    setNotice('');
    setShiftId(shiftChoices[0]?.id ?? '');
    setLineId(lineChoices[0]?.id ?? '');
    setSkuId(skuChoices[0]?.id ?? '');
    setDrafts([emptyDraft(0)]);
    setActiveKey(0);
    setModalOpen(true);
  };

  const updateDraft = (key: number, patch: Partial<WoDraft>): void => {
    setDrafts((items) => items.map((item) => (item.key === key ? { ...item, ...patch } : item)));
  };

  const startProduction = async (): Promise<void> => {
    const workOrders = drafts.map(toInput).filter((item) => item.number && item.client);
    if (workOrders.length === 0) {
      setError('Isi minimal satu WO lengkap (nomor & klien).');
      return;
    }
    if (workOrders.some((item) => item.targetQty <= 0)) {
      setError('Target pcs setiap WO harus lebih dari nol.');
      return;
    }
    const activeNumber = toInput(drafts.find((item) => item.key === activeKey) ?? drafts[0]);
    setBusy(true);
    setError('');
    try {
      const response = await fetch('/api/v1/production/start', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          shiftId,
          lineId,
          skuId,
          workOrders,
          activeWorkOrderNumber: activeNumber.number || workOrders[0].number,
        }),
      });
      if (!response.ok) {
        setError(await readError(response, 'Sesi produksi gagal dimulai.'));
        return;
      }
      setProduction((await response.json()) as ProductionContext);
      setModalOpen(false);
      setNotice('Sesi produksi aktif. Layar scan akan memakai konteks ini.');
    } catch {
      setError('Tidak dapat menghubungi server.');
    } finally {
      setBusy(false);
    }
  };

  const switchWorkOrder = async (number: string): Promise<void> => {
    setBusy(true);
    setError('');
    try {
      const response = await fetch('/api/v1/production/active-work-order', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ number }),
      });
      if (!response.ok) {
        setError(await readError(response, 'Ganti WO gagal.'));
        return;
      }
      setProduction((await response.json()) as ProductionContext);
      setNotice(`WO aktif sekarang ${number}. Layar scan yang masih membuka konteks lama harus dimuat ulang.`);
    } finally {
      setBusy(false);
    }
  };

  const addWorkOrder = async (): Promise<void> => {
    const input = toInput(addDraft);
    if (!input.number || !input.client || input.targetQty <= 0) {
      setError('WO baru butuh nomor, klien, dan target pcs > 0.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      const response = await fetch('/api/v1/production/work-orders', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(input),
      });
      if (!response.ok) {
        setError(await readError(response, 'Penambahan WO gagal.'));
        return;
      }
      setProduction((await response.json()) as ProductionContext);
      setAddDraft(emptyDraft(0));
      setAddWoOpen(false);
      setNotice(`WO ${input.number} ditambahkan ke sesi.`);
    } finally {
      setBusy(false);
    }
  };

  const endSession = async (): Promise<void> => {
    setBusy(true);
    setError('');
    try {
      const response = await fetch('/api/v1/production/end', { method: 'DELETE' });
      if (!response.ok) {
        setError(await readError(response, 'Mengakhiri sesi gagal.'));
        return;
      }
      setProduction(null);
      setNotice('Sesi produksi diakhiri. Mulai sesi baru untuk melanjutkan produksi.');
    } finally {
      setBusy(false);
    }
  };

  if (loading) {
    return (
      <section className="public-card control-room-session">
        <p className="helper">Memuat konteks produksi…</p>
      </section>
    );
  }

  return (
    <section className="public-card control-room-session">
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap', alignItems: 'center' }}>
        <div>
          <div className="eyebrow">SESI PRODUKSI</div>
          <h2 style={{ margin: '6px 0 2px', fontSize: 18 }}>
            {production ? `Aktif — ${production.activeWorkOrderNumber ?? 'tanpa WO'}` : 'Belum ada sesi produksi'}
          </h2>
          <p className="lead" style={{ marginBottom: 0 }}>
            {production
              ? `${production.skuName} (${production.familyCode}) • Line ${production.lineName} • Shift ${production.shiftName}`
              : canOperate
                ? 'Klik "Mulai produksi" untuk memilih shift, line, SKU, dan work order hari ini.'
                : 'Akun ini tidak memiliki hak operasi lantai produksi.'}
            {production ? ` • konteks v${production.contextVersion}` : ''}
          </p>
        </div>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          {!production && canOperate ? (
            <button type="button" className="hero-button primary" onClick={openModal} disabled={busy}>
              <Icon name="check" size={14} /> Mulai produksi
            </button>
          ) : null}
          {production && canOperate ? (
            <button type="button" className="hero-button ghost" style={{ borderColor: '#a30f2d', color: '#a30f2d' }}
              onClick={() => void endSession()} disabled={busy}>
              Akhiri sesi
            </button>
          ) : null}
        </div>
      </div>

      {error ? <div className="scan-alert error" style={{ marginTop: 14 }}>{error}</div> : null}
      {notice && !error ? <div className="scan-alert ok" style={{ marginTop: 14 }}>{notice}</div> : null}

      {production ? (
        <div style={{ marginTop: 16 }}>
          <Panel
            title={`Work order dalam sesi (${production.workOrders.length})`}
            actions={
              canOperate ? (
                <button type="button" onClick={() => { setAddWoOpen((open) => !open); setError(''); }}>
                  <b aria-hidden="true">+</b> Tambah WO
                </button>
              ) : null
            }
          >
            {addWoOpen ? (
              <div className="login-grid-2" style={{ marginBottom: 14 }}>
                <label className="login-field">
                  <span className="login-label">Nomor WO</span>
                  <input className="login-input mono" value={addDraft.number}
                    onChange={(event) => setAddDraft({ ...addDraft, number: event.target.value.toUpperCase() })}
                    placeholder="WO-2026-05" />
                </label>
                <label className="login-field">
                  <span className="login-label">Klien</span>
                  <input className="login-input" value={addDraft.client}
                    onChange={(event) => setAddDraft({ ...addDraft, client: event.target.value })} />
                </label>
                <label className="login-field">
                  <span className="login-label">Referensi Pengiriman</span>
                  <input className="login-input" value={addDraft.shippingReference}
                    onChange={(event) => setAddDraft({ ...addDraft, shippingReference: event.target.value })}
                    placeholder="opsional" />
                </label>
                <label className="login-field">
                  <span className="login-label">Target Pcs</span>
                  <input className="login-input" type="number" min="1" value={addDraft.targetQty}
                    onChange={(event) => setAddDraft({ ...addDraft, targetQty: event.target.value })} />
                </label>
                <div style={{ gridColumn: '1 / -1' }}>
                  <button type="button" className="btn-primary" onClick={() => void addWorkOrder()} disabled={busy}>
                    Simpan WO
                  </button>
                </div>
              </div>
            ) : null}

            <div className="list">
              {production.workOrders.map((wo: WorkOrderInfo) => {
                const isActive = wo.number === production.activeWorkOrderNumber;
                return (
                  <div key={wo.id} className={cn('list-row', isActive && 'is-active')}>
                    <div>
                      <b className="mono">{wo.number}</b>
                      <p>{wo.client} • Ref kirim: {wo.shippingReference || '—'} • Target {formatNumber(wo.targetQty)} pcs</p>
                    </div>
                    <div className="list-tail">
                      {isActive ? (
                        <StatusBadge tone="ok">AKTIF</StatusBadge>
                      ) : (
                        canOperate ? (
                          <button type="button" className="link" onClick={() => void switchWorkOrder(wo.number)} disabled={busy}>
                            Jadikan aktif
                          </button>
                        ) : null
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
            <p className="helper" style={{ margin: '10px 0 0' }}>
              Mengganti WO menaikkan versi konteks; layar scan yang masih memakai konteks lama akan
              ditolak backend sampai konteks dimuat ulang.
            </p>
          </Panel>
        </div>
      ) : null}

      {modalOpen ? (
        <div className="ltn-modal-backdrop" role="dialog" aria-modal="true" aria-label="Mulai produksi">
          <div className="ltn-modal-card">
            <div className="dialog-head">
              <h2>Mulai produksi</h2>
              <button type="button" className="topbar-icon" onClick={() => setModalOpen(false)} aria-label="Tutup">
                <Icon name="close" size={14} />
              </button>
            </div>

            {!catalog ? (
              <EmptyState>Master produksi belum tersedia. Pastikan backend berjalan dan migrasi sudah diterapkan.</EmptyState>
            ) : (
              <>
                <div className="login-grid-2">
                  <label className="login-field">
                    <span className="login-label">Shift Kerja</span>
                    <select className="login-input" value={shiftId} onChange={(event) => setShiftId(event.target.value)}>
                      {shiftChoices.map((item) => (
                        <option key={item.id} value={item.id}>{item.code} — {item.name}</option>
                      ))}
                    </select>
                  </label>
                  <label className="login-field">
                    <span className="login-label">Line / Mesin</span>
                    <select className="login-input" value={lineId} onChange={(event) => setLineId(event.target.value)}>
                      {lineChoices.map((item) => (
                        <option key={item.id} value={item.id}>{item.code} — {item.name}</option>
                      ))}
                    </select>
                  </label>
                  <label className="login-field" style={{ gridColumn: '1 / -1' }}>
                    <span className="login-label">Model / SKU</span>
                    <select className="login-input" value={skuId} onChange={(event) => setSkuId(event.target.value)}>
                      {skuChoices.map((item) => (
                        <option key={item.id} value={item.id}>{item.name}</option>
                      ))}
                    </select>
                  </label>
                </div>

                <div className="eyebrow" style={{ marginTop: 14 }}>WORK ORDER HARI INI</div>
                {drafts.map((draft) => (
                  <div className="login-grid-2 wo-row" key={draft.key}>
                    <label className="login-field">
                      <span className="login-label">Nomor WO</span>
                      <input className="login-input mono" value={draft.number}
                        onChange={(event) => updateDraft(draft.key, { number: event.target.value.toUpperCase() })}
                        placeholder="WO-2026-05" />
                    </label>
                    <label className="login-field">
                      <span className="login-label">Klien</span>
                      <input className="login-input" value={draft.client}
                        onChange={(event) => updateDraft(draft.key, { client: event.target.value })} />
                    </label>
                    <label className="login-field">
                      <span className="login-label">Referensi Pengiriman</span>
                      <input className="login-input" value={draft.shippingReference}
                        onChange={(event) => updateDraft(draft.key, { shippingReference: event.target.value })}
                        placeholder="opsional" />
                    </label>
                    <label className="login-field">
                      <span className="login-label">Target Pcs</span>
                      <input className="login-input" type="number" min="1" value={draft.targetQty}
                        onChange={(event) => updateDraft(draft.key, { targetQty: event.target.value })} />
                    </label>
                    <div style={{ gridColumn: '1 / -1', display: 'flex', gap: 12, alignItems: 'center' }}>
                      <label className="login-remember" style={{ margin: 0 }}>
                        <input type="radio" name="active-wo" checked={activeKey === draft.key}
                          onChange={() => setActiveKey(draft.key)} />
                        Jadikan WO aktif
                      </label>
                      {drafts.length > 1 ? (
                        <button type="button" className="link"
                          onClick={() => setDrafts((items) => items.filter((item) => item.key !== draft.key))}>
                          Hapus baris
                        </button>
                      ) : null}
                    </div>
                  </div>
                ))}

                <button type="button" className="hero-button ghost"
                  style={{ borderColor: '#cbd5e1', color: '#161b41', marginTop: 4 }}
                  onClick={() => {
                    const key = Math.max(...drafts.map((item) => item.key)) + 1;
                    setDrafts((items) => [...items, emptyDraft(key)]);
                  }}>
                  <b aria-hidden="true" style={{ marginRight: 6 }}>+</b> Tambah WO
                </button>

                <div className="form-actions" style={{ marginTop: 16 }}>
                  <button type="button" className="btn-primary" onClick={() => void startProduction()} disabled={busy}>
                    {busy ? 'MEMULAI…' : 'Mulai sesi produksi'}
                  </button>
                  <button type="button" className="hero-button ghost" style={{ borderColor: '#cbd5e1', color: '#161b41' }}
                    onClick={() => setModalOpen(false)} disabled={busy}>
                    Batal
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      ) : null}
    </section>
  );
}
