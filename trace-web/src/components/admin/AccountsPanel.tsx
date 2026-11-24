'use client';

import { matchingRole, ROLE_PRESETS } from '@/lib/roles';

import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import type { AccountInfo, AuthSessionInfo, PermissionOption } from '@/types';
import { cn } from '@/lib/cn';
import { formatDateDisplay } from '@/lib/formatters';
import { Icon } from '@/components/ui/Icon';
import { EmptyState, Panel, StatusBadge } from '@/components/ui/SharedUi';

interface DraftAccount {
  id: string;
  email: string;
  fullName: string;
  status: string;
  password: string;
  permissions: string[];
}

const EMPTY_DRAFT: DraftAccount = {
  id: '',
  email: '',
  fullName: '',
  status: 'ACTIVE',
  password: '',
  permissions: [],
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
 * Tab "Akun & Izin" — kredensial email/password, hak operasi terpisah per akun,
 * dan sesi login yang dapat dihentikan dari server (Fase 1).
 * Backend menuntut hak admin.accounts; tanpa hak tersebut panel menampilkan alasan penolakan.
 */
export function AccountsPanel() {
  const [permissions, setPermissions] = useState<PermissionOption[]>([]);
  const [accounts, setAccounts] = useState<AccountInfo[]>([]);
  const [sessions, setSessions] = useState<AuthSessionInfo[]>([]);
  const [draft, setDraft] = useState<DraftAccount>(EMPTY_DRAFT);
  const [denied, setDenied] = useState('');
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async (): Promise<void> => {
    setError('');
    try {
      const [permissionResponse, accountResponse, sessionResponse] = await Promise.all([
        fetch('/api/v1/accounts/permissions', { cache: 'no-store' }),
        fetch('/api/v1/accounts', { cache: 'no-store' }),
        fetch('/api/v1/auth/sessions', { cache: 'no-store' }),
      ]);
      if (permissionResponse.ok) {
        setPermissions((await permissionResponse.json()) as PermissionOption[]);
      }
      if (accountResponse.status === 403) {
        setDenied(await readError(accountResponse, 'Akun login ini tidak memiliki hak admin.accounts.'));
        return;
      }
      if (!accountResponse.ok) {
        setDenied(await readError(accountResponse, 'Daftar akun gagal dimuat.'));
        return;
      }
      setDenied('');
      setAccounts((await accountResponse.json()) as AccountInfo[]);
      if (sessionResponse.ok) {
        setSessions((await sessionResponse.json()) as AuthSessionInfo[]);
      }
    } catch {
      setError('Tidak dapat menghubungi server untuk daftar akun.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const togglePermission = (key: string): void => {
    setDraft((current) => ({
      ...current,
      permissions: current.permissions.includes(key)
        ? current.permissions.filter((item) => item !== key)
        : [...current.permissions, key],
    }));
  };

  const edit = (account: AccountInfo): void => {
    setNotice('');
    setError('');
    setDraft({
      id: account.id,
      email: account.email,
      fullName: account.fullName,
      status: account.status,
      password: '',
      permissions: [...account.permissions],
    });
  };

  const submit = async (event: FormEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    setBusy(true);
    setError('');
    setNotice('');
    const creating = draft.id === '';
    const payload = creating
      ? {
          email: draft.email.trim().toLowerCase(),
          fullName: draft.fullName.trim(),
          password: draft.password,
          permissions: draft.permissions,
        }
      : {
          id: draft.id,
          fullName: draft.fullName.trim(),
          status: draft.status,
          permissions: draft.permissions,
          newPassword: draft.password || undefined,
        };
    try {
      const response = await fetch('/api/v1/accounts', {
        method: creating ? 'POST' : 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        setError(await readError(response, creating ? 'Akun gagal dibuat.' : 'Perubahan akun gagal disimpan.'));
        return;
      }
      const saved = (await response.json()) as AccountInfo;
      setNotice(
        creating
          ? `Akun ${saved.email} dibuat${saved.mustChangePassword ? ' dengan wajib ganti password pada login pertama' : ''}.`
          : `Perubahan akun ${saved.email} tersimpan.`,
      );
      setDraft(EMPTY_DRAFT);
      await load();
    } catch {
      setError('Tidak dapat menghubungi server.');
    } finally {
      setBusy(false);
    }
  };

  const revokeSession = async (id: string): Promise<void> => {
    setBusy(true);
    try {
      const response = await fetch(`/api/v1/auth/sessions/${encodeURIComponent(id)}/revoke`, { method: 'POST' });
      if (!response.ok) {
        setError(await readError(response, 'Sesi gagal dihentikan.'));
        return;
      }
      setSessions((current) => current.map((item) => (item.id === id ? { ...item, revoked: true } : item)));
      setNotice('Sesi login dihentikan dari server.');
    } catch {
      setError('Tidak dapat menghubungi server.');
    } finally {
      setBusy(false);
    }
  };

  if (denied) {
    return (
      <Panel title="Akun & Izin">
        <div className="scan-alert error" role="status">{denied}</div>
        <p className="helper">
          Masuk memakai akun dengan hak <code>admin.accounts</code> untuk mengelola akun lain.
        </p>
      </Panel>
    );
  }

  const activeSessions = sessions.filter((item) => !item.revoked);

  return (
    <div className="stack">
      {error ? <div className="scan-alert error" role="status">{error}</div> : null}
      {notice ? <div className="scan-alert ok" role="status">{notice}</div> : null}

      <Panel
        title={draft.id === '' ? 'Tambah akun operator' : `Ubah akun — ${draft.email}`}
        actions={
          <div className="panel-actions">
            <StatusBadge tone="neutral">{permissions.length} jenis izin</StatusBadge>
            <button type="button" onClick={() => setDraft(EMPTY_DRAFT)} disabled={busy}>
              {draft.id === '' ? 'Bersihkan' : 'Batalkan perubahan'}
            </button>
          </div>
        }
      >
        <form onSubmit={submit} className="form-grid">
          <label>
            Email
            <input
              type="email"
              value={draft.email}
              onChange={(event) => setDraft((current) => ({ ...current, email: event.target.value }))}
              placeholder="operator@lelco.co.id"
              autoComplete="off"
              disabled={draft.id !== ''}
              required={draft.id === ''}
            />
            {draft.id !== '' ? <span className="helper">Email tidak dapat diubah — buat akun baru bila perlu.</span> : null}
          </label>
          <label>
            Nama lengkap
            <input
              value={draft.fullName}
              onChange={(event) => setDraft((current) => ({ ...current, fullName: event.target.value }))}
              placeholder="Nama operator"
              required
            />
          </label>
          <label>
            {draft.id === '' ? 'Kata sandi awal' : 'Reset kata sandi (opsional)'}
            <input
              type="password"
              value={draft.password}
              onChange={(event) => setDraft((current) => ({ ...current, password: event.target.value }))}
              autoComplete="new-password"
              placeholder={draft.id === '' ? 'Minimal 8 karakter' : 'Kosongkan bila tidak diganti'}
              required={draft.id === ''}
            />
            <span className="helper">
              Disimpan sebagai hash oleh backend. Akun baru / hasil reset wajib ganti sandi saat login berikutnya.
            </span>
          </label>
          {draft.id !== '' ? (
            <label>
              Status akun
              <select
                value={draft.status}
                onChange={(event) => setDraft((current) => ({ ...current, status: event.target.value }))}
              >
                <option value="ACTIVE">AKTIF</option>
                <option value="INACTIVE">NONAKTIF</option>
              </select>
              <span className="helper">Akun nonaktif ditolak saat login dan sesi berjalan berhenti berlaku.</span>
            </label>
          ) : null}
          <div style={{ gridColumn: '1 / -1' }}>
            <label>Preset role
              <select value={matchingRole(draft.permissions)?.label ?? ''} onChange={(event) => {
                const role = ROLE_PRESETS.find((item) => item.label === event.target.value);
                if (role) setDraft((current) => ({ ...current, permissions: [...role.permissions] }));
              }}>
                <option value="">Izin khusus</option>
                {ROLE_PRESETS.map((role) => <option key={role.label} value={role.label}>{role.label}</option>)}
              </select>
            </label>
            <span className="helper" style={{ display: 'block', marginBottom: 6 }}>
              Hak operasi — centang tiap pos yang boleh dikerjakan akun ini
            </span>
            <div className="chip-row">
              {permissions.map((item) => {
                const active = draft.permissions.includes(item.key);
                return (
                  <button
                    key={item.key}
                    type="button"
                    className={cn('chip', active && 'ok')}
                    onClick={() => togglePermission(item.key)}
                    aria-pressed={active}
                    title={item.key}
                  >
                    {active ? '✓ ' : ''}
                    {item.label}
                  </button>
                );
              })}
            </div>
          </div>
          <div className="form-actions" style={{ gridColumn: '1 / -1' }}>
            <button type="submit" className="btn-primary" disabled={busy}>
              <Icon name="check" size={14} />
              {draft.id === '' ? 'Buat akun' : 'Simpan perubahan'}
            </button>
          </div>
        </form>
      </Panel>

      <Panel
        title="Daftar akun"
        actions={
          <div className="panel-actions">
            <StatusBadge tone="neutral">{accounts.length} akun</StatusBadge>
            <button type="button" onClick={() => void load()} disabled={busy}>
              <Icon name="refresh" size={14} /> Muat ulang
            </button>
          </div>
        }
      >
        {loading ? (
          <EmptyState>Memuat daftar akun…</EmptyState>
        ) : accounts.length === 0 ? (
          <EmptyState>Belum ada akun terdaftar.</EmptyState>
        ) : (
          <div className="list">
            {accounts.map((account) => (
              <div className="list-row" key={account.id}>
                <div>
                  <b>{account.fullName}</b>
                  <p className="mono">{account.email}</p>
                  <div className="chip-row" style={{ marginTop: 4 }}>
                    {account.permissions.length === 0 ? (
                      <span className="helper">Tanpa hak operasi</span>
                    ) : (
                      account.permissions.map((key) => (
                        <span key={key} className="chip">
                          {permissions.find((item) => item.key === key)?.label ?? key}
                        </span>
                      ))
                    )}
                  </div>
                </div>
                <div className="list-tail">
                  <StatusBadge tone={account.status === 'ACTIVE' ? 'ok' : 'ng'}>{account.status}</StatusBadge>
                  {account.mustChangePassword ? (
                    <StatusBadge tone="rework">Wajib ganti sandi</StatusBadge>
                  ) : null}
                  <button type="button" onClick={() => edit(account)} disabled={busy}>
                    Ubah
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </Panel>

      <Panel title="Sesi login aktif" actions={<StatusBadge tone="neutral">{activeSessions.length} sesi</StatusBadge>}>
        {activeSessions.length === 0 ? (
          <EmptyState>Tidak ada sesi login yang sedang berjalan.</EmptyState>
        ) : (
          <div className="list">
            {activeSessions.map((session) => (
              <div className="list-row" key={session.id}>
                <div>
                  <b>{session.email}</b>
                  <p>
                    Masuk {formatDateDisplay(session.createdAtUtc)} • terakhir aktif{' '}
                    {formatDateDisplay(session.lastSeenAtUtc)} • kedaluwarsa {formatDateDisplay(session.expiresAtUtc)}
                  </p>
                </div>
                <div className="list-tail">
                  <button type="button" onClick={() => void revokeSession(session.id)} disabled={busy}>
                    <Icon name="logout" size={14} /> Hentikan
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
        <p className="helper" style={{ marginBottom: 0 }}>
          Menghentikan sesi menghapus token di server — perangkat terkait harus login ulang.
        </p>
      </Panel>
    </div>
  );
}
