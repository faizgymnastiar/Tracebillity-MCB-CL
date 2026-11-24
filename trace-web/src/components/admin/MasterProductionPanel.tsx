'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import type { MasterCatalog, MasterEntity, MasterItem } from '@/types';
import { cn } from '@/lib/cn';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface MasterDraft {
  id: string;
  code: string;
  name: string;
  familyId: string;
  stageId: string;
  groupCode: string;
  seq: string;
  status: string;
}

const ENTITY_LABELS: Record<MasterEntity, string> = {
  family: 'Keluarga produk',
  sku: 'SKU / model',
  shift: 'Shift',
  line: 'Line / mesin',
  stage: 'Tahap & pos NG',
  defect: 'Daftar defect',
};

const ENTITY_HINTS: Record<MasterEntity, string> = {
  family: 'Induk SKU dan tahap — satu keluarga berarti satu set tahap/defect yang sah.',
  sku: 'Kode model yang dipakai memilih sesi produksi.',
  shift: 'Pilihan shift pada modal Mulai produksi.',
  line: 'Pilihan line/mesin pada modal Mulai produksi.',
  stage: 'Pos pemeriksaan (ST-xx) tempat defect dicatat.',
  defect: 'ID defect tersendiri walau kode grup (DF-xx) berulang di pos berbeda.',
};

const EMPTY_DRAFT: MasterDraft = {
  id: '',
  code: '',
  name: '',
  familyId: '',
  stageId: '',
  groupCode: '',
  seq: '',
  status: 'ACTIVE',
};

async function readError(response: Response, fallback: string): Promise<string> {
  try {
    const payload = (await response.json()) as { error?: string };
    return payload.error ?? fallback;
  } catch {
    return fallback;
  }
}

/**
 * Tab "Master Produksi" — SSOT §2 dikelola di basis data (keluarga, SKU, shift, line,
 * tahap, defect). Menonaktifkan opsi hanya menghentikan pilihan baru; transaksi lama
 * tetap memakai nama/kode hasil snapshot. Butuh hak admin.master di backend.
 */
export function MasterProductionPanel() {
  const [catalog, setCatalog] = useState<MasterCatalog | null>(null);
  const [entity, setEntity] = useState<MasterEntity>('family');
  const [draft, setDraft] = useState<MasterDraft>(EMPTY_DRAFT);
  const [denied, setDenied] = useState('');
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async (): Promise<void> => {
    try {
      const response = await fetch('/api/v1/master/catalog', { cache: 'no-store' });
      if (response.status === 403) {
        setDenied(await readError(response, 'Akun login ini tidak memiliki hak admin.master.'));
        return;
      }
      if (!response.ok) {
        setDenied(await readError(response, 'Katalog master gagal dimuat.'));
        return;
      }
      setDenied('');
      setCatalog((await response.json()) as MasterCatalog);
    } catch {
      setError('Tidak dapat menghubungi server master.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const families = catalog?.families ?? [];
  const stages = catalog?.stages ?? [];

  const items = useMemo<MasterItem[]>(() => {
    if (!catalog) return [];
    switch (entity) {
      case 'family':
        return catalog.families;
      case 'sku':
        return catalog.skus;
      case 'shift':
        return catalog.shifts;
      case 'line':
        return catalog.lines;
      case 'stage':
        return catalog.stages;
      case 'defect':
        return catalog.stages.flatMap((stage) => stage.defects ?? []);
      default:
        return [];
    }
  }, [catalog, entity]);

  /** Tahap yang sah untuk defect: hanya milik keluarga yang sama dengan tahap terpilih. */
  const stageChoices = useMemo(() => {
    if (!draft.stageId) return stages;
    const current = stages.find((stage) => stage.id === draft.stageId);
    return current ? stages.filter((stage) => (stage.familyId ?? '') === (current.familyId ?? '')) : stages;
  }, [stages, draft.stageId]);

  const pickEntity = (next: MasterEntity): void => {
    setEntity(next);
    setDraft(EMPTY_DRAFT);
    setError('');
  };

  const edit = (item: MasterItem): void => {
    setError('');
    setNotice('');
    setDraft({
      id: item.id,
      code: item.code ?? '',
      name: item.name,
      familyId: item.familyId ?? '',
      stageId: item.stageId ?? '',
      groupCode: item.groupCode ?? '',
      seq: item.seq === undefined ? '' : String(item.seq),
      status: item.status,
    });
  };

  const submit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    setBusy(true);
    setError('');
    setNotice('');
    const payload: Record<string, string | number | undefined> = {
      id: draft.id || undefined,
      name: draft.name.trim(),
      status: draft.status,
    };
    if (entity === 'defect') {
      payload.stageId = draft.stageId || undefined;
      payload.groupCode = draft.groupCode.trim().toUpperCase() || undefined;
    } else {
      payload.code = draft.code.trim().toUpperCase();
      if (entity === 'sku' || entity === 'stage') payload.familyId = draft.familyId || undefined;
      if (entity === 'stage') payload.seq = Number.parseInt(draft.seq, 10) || undefined;
    }
    try {
      const response = await fetch(`/api/v1/master/catalog/${entity}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        setError(await readError(response, 'Master gagal disimpan.'));
        return;
      }
      setCatalog((await response.json()) as MasterCatalog);
      setNotice(`${ENTITY_LABELS[entity]} "${draft.name.trim()}" tersimpan dan langsung tersedia di layar produksi.`);
      setDraft(EMPTY_DRAFT);
    } catch {
      setError('Tidak dapat menghubungi server.');
    } finally {
      setBusy(false);
    }
  };

  const setStatus = async (item: MasterItem, status: 'ACTIVE' | 'INACTIVE'): Promise<void> => {
    setBusy(true);
    setError('');
    try {
      const response = await fetch(`/api/v1/master/catalog/${entity}/${encodeURIComponent(item.id)}/status`, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ status }),
      });
      if (!response.ok) {
        setError(await readError(response, 'Perubahan status gagal.'));
        return;
      }
      setCatalog((await response.json()) as MasterCatalog);
      setNotice(
        status === 'INACTIVE'
          ? `"${item.name}" dinonaktifkan — tidak muncul lagi pada pilihan baru, riwayat lama tetap utuh.`
          : `"${item.name}" diaktifkan kembali.`,
      );
    } catch {
      setError('Tidak dapat menghubungi server.');
    } finally {
      setBusy(false);
    }
  };

  if (denied) {
    return (
      <Panel title="Master Produksi">
        <div className="scan-alert error" role="status">{denied}</div>
        <p className="helper">
          Masuk memakai akun dengan hak <code>admin.master</code> untuk mengubah master produksi.
        </p>
      </Panel>
    );
  }

  const needsCode = entity !== 'defect';

  return (
    <div className="stack">
      {error ? <div className="scan-alert error" role="status">{error}</div> : null}
      {notice ? <div className="scan-alert ok" role="status">{notice}</div> : null}

      <div className="seg-tabs" role="tablist" aria-label="Entitas master">
        {(Object.keys(ENTITY_LABELS) as MasterEntity[]).map((key) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={entity === key}
            className={cn('seg-tab', entity === key && 'active')}
            onClick={() => pickEntity(key)}
            title={ENTITY_HINTS[key]}
          >
            {ENTITY_LABELS[key]}
          </button>
        ))}
      </div>

      <Panel
        title={draft.id === '' ? `Tambah ${ENTITY_LABELS[entity].toLowerCase()}` : `Ubah — ${draft.name}`}
        actions={
          <div className="panel-actions">
            <StatusBadge tone="neutral">{items.length} entri</StatusBadge>
            <button type="button" onClick={() => setDraft(EMPTY_DRAFT)} disabled={busy}>
              {draft.id === '' ? 'Bersihkan' : 'Batalkan'}
            </button>
          </div>
        }
      >
        <p className="helper" style={{ marginTop: 0 }}>{ENTITY_HINTS[entity]}</p>
        <form onSubmit={submit} className="form-grid">
          {needsCode ? (
            <label>
              Kode
              <input
                className="mono"
                value={draft.code}
                onChange={(event) => setDraft((current) => ({ ...current, code: event.target.value.toUpperCase() }))}
                placeholder={entity === 'sku' ? 'MCBCL-P1-32A' : entity === 'stage' ? 'ST-05' : 'SHIFT-1'}
                required
              />
              <span className="helper">Kode menjadi bagian dari format serial dan label.</span>
            </label>
          ) : (
            <label>
              Kode grup defect
              <input
                className="mono"
                value={draft.groupCode}
                onChange={(event) => setDraft((current) => ({ ...current, groupCode: event.target.value.toUpperCase() }))}
                placeholder="DF-03 (kosongkan untuk mengikuti kode tahap)"
              />
              <span className="helper">Boleh berulang antar pos; setiap baris tetap punya ID sendiri.</span>
            </label>
          )}
          <label>
            Nama
            <input
              value={draft.name}
              onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))}
              placeholder={entity === 'defect' ? 'Retak pada housing' : 'MCB CL P1 32 A'}
              required
            />
          </label>
          {entity === 'sku' || entity === 'stage' ? (
            <label>
              Keluarga produk
              <select
                value={draft.familyId}
                onChange={(event) => setDraft((current) => ({ ...current, familyId: event.target.value }))}
                required
              >
                <option value="">— pilih keluarga —</option>
                {families.map((family) => (
                  <option key={family.id} value={family.id}>
                    {family.code} — {family.name}
                    {family.status === 'INACTIVE' ? ' (nonaktif)' : ''}
                  </option>
                ))}
              </select>
              <span className="helper">Tahap/defect keluarga lain ditolak backend saat dipakai lintas sesi.</span>
            </label>
          ) : null}
          {entity === 'stage' ? (
            <label>
              Urutan tahap
              <input
                type="number"
                min={1}
                className="mono"
                value={draft.seq}
                onChange={(event) => setDraft((current) => ({ ...current, seq: event.target.value }))}
                placeholder="otomatis dari kode ST-xx"
              />
            </label>
          ) : null}
          {entity === 'defect' ? (
            <label>
              Tahap / pos NG
              <select
                value={draft.stageId}
                onChange={(event) => setDraft((current) => ({ ...current, stageId: event.target.value }))}
                required
              >
                <option value="">— pilih tahap —</option>
                {stageChoices.map((stage) => (
                  <option key={stage.id} value={stage.id}>
                    {stage.code} — {stage.name}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          <div className="form-actions" style={{ gridColumn: '1 / -1' }}>
            <button type="submit" className="btn-primary" disabled={busy}>
              <Icon name="check" size={14} />
              {draft.id === '' ? 'Simpan master' : 'Simpan perubahan'}
            </button>
          </div>
        </form>
      </Panel>

      <Panel
        title={`Daftar ${ENTITY_LABELS[entity].toLowerCase()}`}
        actions={
          <div className="panel-actions">
            <button type="button" onClick={() => void load()} disabled={busy}>
              <Icon name="refresh" size={14} /> Muat ulang
            </button>
          </div>
        }
      >
        {loading ? (
          <EmptyState>Memuat katalog master…</EmptyState>
        ) : items.length === 0 ? (
          <EmptyState>Belum ada data pada entitas ini.</EmptyState>
        ) : (
          <div className="list">
            {items.map((item) => {
              const parentStage =
                entity === 'defect'
                  ? stages.find((stage) => (stage.defects ?? []).some((defect) => defect.id === item.id))
                  : undefined;
              const family =
                item.familyId && entity !== 'defect'
                  ? families.find((entry) => entry.id === item.familyId)
                  : undefined;
              return (
                <div className="list-row" key={item.id}>
                  <div>
                    <b className="mono">{entity === 'defect' ? item.groupCode || item.code : item.code}</b>
                    <p>
                      {item.name}
                      {family ? ` • keluarga ${family.code}` : ''}
                      {parentStage ? ` • tahap ${parentStage.code}` : ''}
                      {entity === 'stage' && (item.defects ?? []).length > 0
                        ? ` • ${(item.defects ?? []).length} defect`
                        : ''}
                    </p>
                  </div>
                  <div className="list-tail">
                    <StatusBadge tone={item.status === 'ACTIVE' ? 'ok' : 'neutral'}>{item.status}</StatusBadge>
                    <button type="button" onClick={() => edit(item)} disabled={busy}>
                      Ubah
                    </button>
                    {item.status === 'ACTIVE' ? (
                      <button type="button" onClick={() => void setStatus(item, 'INACTIVE')} disabled={busy}>
                        Nonaktifkan
                      </button>
                    ) : (
                      <button type="button" onClick={() => void setStatus(item, 'ACTIVE')} disabled={busy}>
                        Aktifkan
                      </button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}
        <p className="helper" style={{ marginBottom: 0 }}>
          Master yang sudah dipakai transaksi tidak dihapus — nonaktifkan agar hilang dari pilihan baru
          sementara riwayat tetap terbaca.
        </p>
      </Panel>
    </div>
  );
}
