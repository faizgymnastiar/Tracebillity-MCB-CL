'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { FormEvent } from 'react';
import type {
  ExternalPrinterMode,
  MasterListKey,
  NgStation,
  PublicConfig,
} from '@/types';
import { ALL_EXPORT_COLUMNS, MASTER_LIST_KEYS, MASTER_LIST_LABELS } from '@/lib/config';
import { cn } from '@/lib/cn';
import { formatNumber } from '@/lib/formatters';
import { Icon } from '@/components/ui/Icon';
import { PageHeader, Panel, StatusBadge } from '@/components/ui/SharedUi';
import { AccountsPanel } from '@/components/admin/AccountsPanel';
import { MasterProductionPanel } from '@/components/admin/MasterProductionPanel';

type AdminTab = 'brand' | 'texts' | 'codes' | 'printer' | 'akun' | 'masterprod' | 'master' | 'export';

type MasterLists = Record<MasterListKey, string[]>;

/** Master yang kini bersumber dari basis data (tab Master Produksi), bukan lagi daftar teks. */
const DB_MANAGED_MASTER_KEYS: MasterListKey[] = ['skus', 'lines', 'shifts'];
const LEGACY_MASTER_KEYS = MASTER_LIST_KEYS.filter((key) => !DB_MANAGED_MASTER_KEYS.includes(key));

/** Payload config tidak boleh lagi memuat daftar master — backend menolaknya saat enforcement aktif. */
function configPayload(source: PublicConfig): Record<string, unknown> {
  const payload: Record<string, unknown> = { ...source };
  for (const key of DB_MANAGED_MASTER_KEYS) delete payload[key];
  return payload;
}

interface SuperAdminWorkspaceProps {
  initialConfig: PublicConfig;
  admin: boolean;
  canManageAccounts: boolean;
  profileName: string;
}

const TABS: Array<{ id: AdminTab; label: string; hint: string }> = [
  { id: 'brand', label: 'Brand & publik', hint: 'Identitas perusahaan, hero, dan footer' },
  { id: 'texts', label: 'Teks UI', hint: 'Judul halaman scan, database, dan label' },
  { id: 'codes', label: 'Kode & kapasitas', hint: 'Format box, padding, dan batas rework' },
  { id: 'printer', label: 'Barcode & printer', hint: 'Payload 2D, delimiter, dan bridge' },
  { id: 'akun', label: 'Akun & izin', hint: 'Akun login, hak operasi terpisah, sesi aktif' },
  { id: 'masterprod', label: 'Master produksi', hint: 'Keluarga, SKU, shift, line, tahap, defect (basis data)' },
  { id: 'master', label: 'Master data', hint: 'Lot, operator, tujuan, tindakan rework' },
  { id: 'export', label: 'Export studio', hint: 'Kolom default laporan CSV' },
];

const PRINTER_MODES: ExternalPrinterMode[] = ['BARTENDER_CSV', 'JSON_API', 'ZPL_RAW', 'TSPL_RAW'];

interface FieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: string;
  mono?: boolean;
  placeholder?: string;
  wide?: boolean;
  rows?: number;
}

function Field({ label, value, onChange, hint, mono, placeholder, wide, rows }: FieldProps) {
  return (
    <label style={wide ? { gridColumn: '1 / -1' } : undefined}>
      {label}
      {rows ? (
        <textarea
          rows={rows}
          value={value}
          placeholder={placeholder}
          onChange={(event) => onChange(event.target.value)}
          className={mono ? 'mono' : undefined}
          style={{ padding: '7px 10px', borderRadius: 4, border: '1px solid #cbd3de', resize: 'vertical' }}
        />
      ) : (
        <input
          value={value}
          placeholder={placeholder}
          onChange={(event) => onChange(event.target.value)}
          className={mono ? 'mono' : undefined}
        />
      )}
      {hint ? <span className="helper">{hint}</span> : null}
    </label>
  );
}

function NumberField({ label, value, onChange, hint }: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  hint?: string;
}) {
  return (
    <label>
      {label}
      <input
        type="number"
        min={1}
        value={value}
        onChange={(event) => onChange(Number(event.target.value) || 0)}
        className="mono"
      />
      {hint ? <span className="helper">{hint}</span> : null}
    </label>
  );
}

function CheckField({ label, checked, onChange, hint }: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
  hint?: string;
}) {
  return (
    <label style={{ justifyContent: 'flex-start' }}>
      <span style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12.5, color: '#161b41', fontWeight: 600 }}>
        <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
        {label}
      </span>
      {hint ? <span className="helper">{hint}</span> : null}
    </label>
  );
}

export function SuperAdminWorkspace({ initialConfig, admin, canManageAccounts, profileName }: SuperAdminWorkspaceProps) {
  const router = useRouter();
  const [config, setConfig] = useState<PublicConfig>(initialConfig);
  const [draft, setDraft] = useState<PublicConfig>(initialConfig);
  const [tab, setTab] = useState<AdminTab>('brand');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null);
  const [lists, setLists] = useState<MasterLists | null>(null);
  const [listKey, setListKey] = useState<MasterListKey>('materials');
  const [listValue, setListValue] = useState('');

  const loadLists = useCallback(async (): Promise<void> => {
    try {
      const response = await fetch('/api/v1/master/lists', { cache: 'no-store' });
      const payload = (await response.json()) as { lists: MasterLists };
      setLists(payload.lists);
    } catch {
      setNotice({ tone: 'error', text: 'Master data gagal dimuat.' });
    }
  }, []);

  useEffect(() => {
    if (admin) void loadLists();
  }, [admin, loadLists]);

  const setField = <K extends keyof PublicConfig>(key: K, value: PublicConfig[K]): void => {
    setDraft((current) => ({ ...current, [key]: value }));
  };

  const save = async (): Promise<void> => {
    setBusy(true);
    setNotice(null);
    try {
      const response = await fetch('/api/v1/config', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(configPayload(draft)),
      });
      const result = (await response.json()) as PublicConfig | { error: string };
      if ('error' in result) {
        setNotice({ tone: 'error', text: result.error });
        return;
      }
      setConfig(result);
      setDraft(result);
      setNotice({ tone: 'ok', text: 'Konfigurasi tersimpan dan langsung dipakai seluruh halaman.' });
      router.refresh();
    } catch {
      setNotice({ tone: 'error', text: 'Konfigurasi gagal disimpan.' });
    } finally {
      setBusy(false);
    }
  };

  const reset = async (): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/config/reset', { method: 'POST' });
      const result = (await response.json()) as PublicConfig | { error: string };
      if ('error' in result) {
        setNotice({ tone: 'error', text: result.error });
        return;
      }
      setConfig(result);
      setDraft(result);
      setNotice({ tone: 'ok', text: 'Konfigurasi dikembalikan ke baseline pabrik.' });
      router.refresh();
    } finally {
      setBusy(false);
    }
  };

  const addMaster = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    if (!listValue.trim()) return;
    setBusy(true);
    try {
      const response = await fetch('/api/v1/master/lists', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ key: listKey, value: listValue }),
      });
      const result = (await response.json()) as { lists: MasterLists } | { error: string };
      if ('error' in result) {
        setNotice({ tone: 'error', text: result.error });
        return;
      }
      setLists(result.lists);
      setListValue('');
      setNotice({ tone: 'ok', text: 'Master data ditambahkan.' });
    } finally {
      setBusy(false);
    }
  };

  const removeMaster = async (key: MasterListKey, value: string): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/master/lists', {
        method: 'DELETE',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ key, value }),
      });
      const result = (await response.json()) as { lists: MasterLists } | { error: string };
      if ('error' in result) {
        setNotice({ tone: 'error', text: result.error });
        return;
      }
      setLists(result.lists);
    } finally {
      setBusy(false);
    }
  };

  const updateStationDefect = (stationId: string, next: string): void => {
    const defects = next
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);
    setField(
      'ngStations',
      draft.ngStations.map((station: NgStation) => (station.id === stationId ? { ...station, defects } : station)),
    );
  };

  const toggleSelectedColumn = (key: string): void => {
    setField(
      'selectedColumns',
      draft.selectedColumns.includes(key)
        ? draft.selectedColumns.filter((item) => item !== key)
        : [...draft.selectedColumns, key],
    );
  };

  if (!admin) {
    return (
      <>
        <PageHeader
          eyebrow="GOVERNANCE • SUPER ADMIN"
          title="Super Admin Studio"
          description="Ruang kontrol brand, format kode, printer, dan master data traceability."
          actions={<StatusBadge tone="neutral">Akses terbatas</StatusBadge>}
        />
        <div className="admin-locked">
          <Panel title="Akses konfigurasi">
            <p className="helper">Sesi aktif: <b>{profileName}</b>. Masuk dengan akun yang memiliki izin kelola master.</p>
          </Panel>
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader
        eyebrow="GOVERNANCE • SUPER ADMIN"
        title="Super Admin Studio"
        description={`Konfigurasi aktif untuk ${config.companyName}. Semua perubahan langsung berlaku di seluruh modul.`}
        actions={
          <div className="panel-actions">
            <StatusBadge tone="ok">Admin master</StatusBadge>
            <button type="button" onClick={() => void save()} disabled={busy}>
              <Icon name="check" size={14} /> Simpan
            </button>
            <button type="button" onClick={() => void reset()} disabled={busy}>
              <Icon name="refresh" size={14} /> Reset baseline
            </button>
          </div>
        }
      />

      {notice ? <div className={cn('scan-alert', notice.tone)}>{notice.text}</div> : null}

      <div className="seg-tabs" role="tablist" aria-label="Bagian konfigurasi">
        {TABS.filter((item) => item.id !== 'akun' || canManageAccounts).map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            className={cn('seg-tab', tab === item.id && 'active')}
            onClick={() => setTab(item.id)}
            title={item.hint}
          >
            {item.label}
          </button>
        ))}
      </div>

      {tab === 'brand' ? (
        <Panel title="Identitas brand & halaman publik">
          <div className="form-grid">
            <Field label="Nama perusahaan" value={draft.companyName} onChange={(value) => setField('companyName', value)} />
            <Field label="Subtitle pabrik" value={draft.subtitle} onChange={(value) => setField('subtitle', value)} />
            <Field label="Judul browser" value={draft.browserTitle} onChange={(value) => setField('browserTitle', value)} />
            <Field label="Judul hero" value={draft.heroTitle} onChange={(value) => setField('heroTitle', value)} />
            <Field label="Tombol login" value={draft.btnLoginText} onChange={(value) => setField('btnLoginText', value)} />
            <Field label="Tombol database" value={draft.btnDbText} onChange={(value) => setField('btnDbText', value)} />
            <Field label="Footer atas" value={draft.footerTopText} onChange={(value) => setField('footerTopText', value)} wide />
            <Field label="Footer bawah" value={draft.footerBottomText} onChange={(value) => setField('footerBottomText', value)} wide />
            <Field
              label="Logo data URL"
              value={draft.logoDataUrl}
              onChange={(value) => setField('logoDataUrl', value)}
              hint="Tempel data:image/png;base64,... — kosongkan untuk memakai monogram otomatis."
              mono
              wide
            />
            <Field label="Accent color" value={draft.accentColor} onChange={(value) => setField('accentColor', value)} mono />
            <Field label="Emoji favicon" value={draft.faviconEmoji} onChange={(value) => setField('faviconEmoji', value)} />
          </div>
        </Panel>
      ) : null}

      {tab === 'texts' ? (
        <Panel title="Teks antarmuka operasional">
          <div className="form-grid">
            <Field label="Judul scan OK" value={draft.uiScanOkTitle} onChange={(value) => setField('uiScanOkTitle', value)} />
            <Field label="Badge status scan" value={draft.uiScanStatusBadge} onChange={(value) => setField('uiScanStatusBadge', value)} />
            <Field label="Judul scan NG" value={draft.uiScanNgTitle} onChange={(value) => setField('uiScanNgTitle', value)} />
            <Field label="Judul scan rework" value={draft.uiScanRwTitle} onChange={(value) => setField('uiScanRwTitle', value)} />
            <Field label="Header database" value={draft.uiDbHeaderTitle} onChange={(value) => setField('uiDbHeaderTitle', value)} />
            <Field label="Judul pencarian" value={draft.uiDbSearchTitle} onChange={(value) => setField('uiDbSearchTitle', value)} />
            <Field label="Judul tabel" value={draft.uiDbTableTitle} onChange={(value) => setField('uiDbTableTitle', value)} />
            <Field label="Judul laporan" value={draft.uiReportTitle} onChange={(value) => setField('uiReportTitle', value)} />
            <Field label="Judul halaman login" value={draft.loginTitle} onChange={(value) => setField('loginTitle', value)} />
            <Field label="Tombol login" value={draft.loginSubmitText} onChange={(value) => setField('loginSubmitText', value)} />
            <Field label="Judul label inner" value={draft.labelInnerTitle} onChange={(value) => setField('labelInnerTitle', value)} />
            <Field label="Judul label outer" value={draft.labelOuterTitle} onChange={(value) => setField('labelOuterTitle', value)} />
          </div>
        </Panel>
      ) : null}

      {tab === 'codes' ? (
        <Panel title="Format kode & kapasitas kemasan">
          <div className="form-grid">
            <Field
              label="Format inner box"
              value={draft.innerCodeFormat}
              onChange={(value) => setField('innerCodeFormat', value)}
              hint="Token: {YYYY} {YY} {MM} {DD} {DEST} {LINE} {SEQ}"
              mono
            />
            <Field
              label="Format outer box"
              value={draft.outerCodeFormat}
              onChange={(value) => setField('outerCodeFormat', value)}
              hint="Token: {YYYY} {YY} {MM} {DD} {DEST} {LINE} {SEQ}"
              mono
            />
            <Field
              label="Format tiket rework"
              value={draft.reworkCodeFormat}
              onChange={(value) => setField('reworkCodeFormat', value)}
              hint="Dipakai untuk tiket verifikasi after rework."
              mono
            />
            <Field
              label="Pola SN (regex, opsional)"
              value={draft.snPattern}
              onChange={(value) => setField('snPattern', value)}
              hint="Kosongkan untuk validasi minimal 4 karakter."
              mono
            />
            <NumberField label="Padding sequence" value={draft.seqPadding}
              onChange={(value) => setField('seqPadding', value)} hint="Minimal 2 digit." />
            <NumberField label="Kapasitas inner box" value={draft.innerCap}
              onChange={(value) => setField('innerCap', value)} hint="Pcs per inner box." />
            <NumberField label="Kapasitas outer box" value={draft.outerCap}
              onChange={(value) => setField('outerCap', value)} hint="Jumlah inner per outer." />
            <NumberField label="Batas siklus rework" value={draft.maxReworkCycles}
              onChange={(value) => setField('maxReworkCycles', value)} hint="Unit melewati batas otomatis scrap." />
            <CheckField label="Reset harian otomatis" checked={draft.dailyReset}
              onChange={(value) => setField('dailyReset', value)} hint="Box disegel parsial saat ganti hari." />
          </div>
          <div className="chip-row" style={{ marginTop: 14 }}>
            <span className="helper">Pratinjau pola:</span>
            <code>{draft.innerCodeFormat}</code>
            <code>{draft.outerCodeFormat}</code>
            <code>{draft.reworkCodeFormat}</code>
          </div>
        </Panel>
      ) : null}

      {tab === 'printer' ? (
        <div className="stack">
          <Panel title="Barcode 2D & label">
            <div className="form-grid">
              <label>
                Tipe barcode 2D
                <select
                  value={draft.barcode2DType}
                  onChange={(event) => setField('barcode2DType', event.target.value === 'DATAMATRIX' ? 'DATAMATRIX' : 'QR_MATRIX')}
                >
                  <option value="QR_MATRIX">QR Matrix</option>
                  <option value="DATAMATRIX">Data Matrix</option>
                </select>
              </label>
              <NumberField label="Ukuran render (px)" value={draft.barcode2DSize}
                onChange={(value) => setField('barcode2DSize', value)} />
              <NumberField label="Lebar label (mm)" value={draft.labelWidthMm}
                onChange={(value) => setField('labelWidthMm', value)} />
              <NumberField label="Tinggi label (mm)" value={draft.labelHeightMm}
                onChange={(value) => setField('labelHeightMm', value)} />
              <Field label="Delimiter payload" value={draft.barcodeDelimiter}
                onChange={(value) => setField('barcodeDelimiter', value)} mono hint="Default |" />
              <Field label="Field payload 2D" value={draft.barcodePayloadFields}
                onChange={(value) => setField('barcodePayloadFields', value)} mono wide
                hint="Token: {BOX_ID} {SKU} {WO} {LOT} {QTY} {DEST} {PRINT_TIME} {LINE} {OPERATOR}" />
            </div>
          </Panel>
          <Panel title="Printer bridge">
            <div className="form-grid">
              <label>
                Mode bridge
                <select
                  value={draft.externalPrinterMode}
                  onChange={(event) => setField('externalPrinterMode', event.target.value as ExternalPrinterMode)}
                >
                  {PRINTER_MODES.map((mode) => (
                    <option key={mode} value={mode}>
                      {mode}
                    </option>
                  ))}
                </select>
              </label>
              <Field label="Webhook printer" value={draft.webhookUrl}
                onChange={(value) => setField('webhookUrl', value)} mono wide />
              <CheckField label="Auto bridge saat box penuh" checked={draft.autoBridgePrint}
                onChange={(value) => setField('autoBridgePrint', value)}
                hint="Bridge otomatis diunduh bersamaan dengan penyegelan box." />
            </div>
          </Panel>
          <Panel title="Station NG & defect">
            <div className="list">
              {draft.ngStations.map((station: NgStation) => (
                <div className="list-row" key={station.id}>
                  <div style={{ flex: 1 }}>
                    <b className="mono">{station.label}</b>
                    <input
                      value={station.defects.join(', ')}
                      onChange={(event) => updateStationDefect(station.id, event.target.value)}
                      style={{ marginTop: 6, width: '100%' }}
                      className="mono"
                    />
                  </div>
                </div>
              ))}
            </div>
            <p className="helper" style={{ marginBottom: 0 }}>
              Pisahkan daftar defect dengan koma. Perubahan ikut tersimpan saat menekan Simpan.
            </p>
          </Panel>
        </div>
      ) : null}

      {tab === 'akun' && canManageAccounts ? <AccountsPanel /> : null}

      {tab === 'masterprod' ? <MasterProductionPanel /> : null}

      {tab === 'master' ? (
        <div className="stack">
          <Panel
            title="Tambah master data"
            actions={<StatusBadge tone="neutral">{lists ? formatNumber(Object.values(lists).flat().length) : '—'} entri</StatusBadge>}
          >
            <p className="helper" style={{ marginTop: 0 }}>
              Model/SKU, Line Mesin, dan Shift Kerja kini disimpan di basis data — kelola lewat tab{' '}
              <button type="button" className="link" onClick={() => setTab('masterprod')}>Master produksi</button>.
            </p>
            <form onSubmit={addMaster} className="scan-form">
              <label style={{ flex: '0 1 220px' }}>
                Kategori
                <select value={listKey} onChange={(event) => setListKey(event.target.value as MasterListKey)}>
                  {LEGACY_MASTER_KEYS.map((key) => (
                    <option key={key} value={key}>
                      {MASTER_LIST_LABELS[key]}
                    </option>
                  ))}
                </select>
              </label>
              <label className="grow">
                Nilai baru
                <input value={listValue} onChange={(event) => setListValue(event.target.value)} placeholder="Contoh: MCB CL P1 - 32 A" />
              </label>
              <button type="submit" className="btn-primary" disabled={busy || !listValue.trim()}>
                <Icon name="check" size={14} /> Tambah
              </button>
            </form>
          </Panel>

          {lists
            ? LEGACY_MASTER_KEYS.map((key) => (
                <Panel
                  key={key}
                  title={MASTER_LIST_LABELS[key]}
                  actions={<StatusBadge tone="neutral">{lists[key].length} entri</StatusBadge>}
                >
                  {lists[key].length === 0 ? (
                    <p className="helper">Belum ada data pada kategori ini.</p>
                  ) : (
                    <div className="chip-row">
                      {lists[key].map((value) => (
                        <span key={value} className="chip">
                          {value}
                          <button
                            type="button"
                            className="link"
                            onClick={() => void removeMaster(key, value)}
                            disabled={busy}
                            aria-label={`Hapus ${value}`}
                          >
                            <Icon name="close" size={12} />
                          </button>
                        </span>
                      ))}
                    </div>
                  )}
                </Panel>
              ))
            : null}
        </div>
      ) : null}

      {tab === 'export' ? (
        <div className="stack">
          <Panel
            title="Kolom default export"
            actions={<StatusBadge tone="neutral">{draft.selectedColumns.length} / {ALL_EXPORT_COLUMNS.length}</StatusBadge>}
          >
            <div className="chip-row">
              {ALL_EXPORT_COLUMNS.map((column) => {
                const active = draft.selectedColumns.includes(column.key);
                return (
                  <button
                    key={column.key}
                    type="button"
                    className={cn('chip', active && 'ok')}
                    onClick={() => toggleSelectedColumn(column.key)}
                    aria-pressed={active}
                  >
                    {active ? '✓ ' : ''}
                    {column.label}
                  </button>
                );
              })}
            </div>
            <p className="helper" style={{ marginTop: 12, marginBottom: 0 }}>
              Pilihan ini menjadi default Export Studio dan endpoint CSV saat parameter kolom tidak dikirim.
            </p>
          </Panel>
          <Panel title="Template laporan" actions={<StatusBadge tone="neutral">{draft.exportTemplates.length} template</StatusBadge>}>
            {draft.exportTemplates.length === 0 ? (
              <p className="helper">Belum ada template tersimpan. Template dapat ditambahkan lewat payload konfigurasi.</p>
            ) : (
              <div className="list">
                {draft.exportTemplates.map((template) => (
                  <div className="list-row" key={template.name}>
                    <div>
                      <b>{template.name}</b>
                      <p>
                        {template.columns.length} kolom • orientasi {template.orientation}
                      </p>
                    </div>
                    <div className="list-tail mono">{template.columns.join(', ')}</div>
                  </div>
                ))}
              </div>
            )}
          </Panel>
        </div>
      ) : null}
    </>
  );
}
