-- Migrasi 0001: skema fondasi produksi (Fase 1)
-- Penyimpanan relasional persisten: akun, izin, sesi login, master produksi,
-- sesi produksi & WO. Semua waktu disimpan UTC.

CREATE TABLE accounts (
    id                   TEXT PRIMARY KEY,
    email                TEXT NOT NULL UNIQUE COLLATE NOCASE,
    full_name            TEXT NOT NULL,
    password_hash        TEXT NOT NULL,
    status               TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE',
    must_change_password INTEGER NOT NULL DEFAULT 0,
    created_at_utc       TEXT NOT NULL,
    updated_at_utc       TEXT NOT NULL
);

CREATE TABLE account_permissions (
    account_id TEXT NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    permission TEXT NOT NULL,
    PRIMARY KEY (account_id, permission)
);

-- Sesi login server-side. Token acak disimpan sebagai hash SHA-256,
-- sehingga sesi dapat diakhiri (revoked) dan kedaluwarsa tanpa membocorkan token.
CREATE TABLE auth_sessions (
    id              TEXT PRIMARY KEY,
    account_id      TEXT NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    token_hash      TEXT NOT NULL UNIQUE,
    created_at_utc  TEXT NOT NULL,
    expires_at_utc  TEXT NOT NULL,
    last_seen_utc   TEXT NOT NULL,
    revoked_at_utc  TEXT
);
CREATE INDEX idx_auth_sessions_account ON auth_sessions (account_id, revoked_at_utc);

CREATE TABLE product_families (
    id     TEXT PRIMARY KEY,
    code   TEXT NOT NULL UNIQUE,
    name   TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE'
);

CREATE TABLE product_skus (
    id        TEXT PRIMARY KEY,
    family_id TEXT NOT NULL REFERENCES product_families (id),
    code      TEXT NOT NULL UNIQUE,
    name      TEXT NOT NULL,
    status    TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE'
);
CREATE INDEX idx_skus_family ON product_skus (family_id);

CREATE TABLE shifts (
    id     TEXT PRIMARY KEY,
    code   TEXT NOT NULL UNIQUE,
    name   TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE'
);

CREATE TABLE lines (
    id     TEXT PRIMARY KEY,
    code   TEXT NOT NULL UNIQUE,
    name   TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE'
);

-- Tahap QC berurutan, dikaitkan ke keluarga produk (SSOT §2).
CREATE TABLE stages (
    id        TEXT PRIMARY KEY,
    family_id TEXT NOT NULL REFERENCES product_families (id),
    code      TEXT NOT NULL,
    name      TEXT NOT NULL,
    seq       INTEGER NOT NULL,
    status    TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE',
    UNIQUE (family_id, code)
);

-- Setiap pilihan defect punya ID tersendiri walau kode grup DF berulang (SSOT §2).
CREATE TABLE defects (
    id         TEXT PRIMARY KEY,
    stage_id   TEXT NOT NULL REFERENCES stages (id),
    group_code TEXT NOT NULL,
    name       TEXT NOT NULL,
    status     TEXT NOT NULL CHECK (status IN ('ACTIVE', 'INACTIVE')) DEFAULT 'ACTIVE',
    UNIQUE (stage_id, group_code, name)
);

CREATE TABLE work_orders (
    id                TEXT PRIMARY KEY,
    -- FK ke production_sessions (dibuat setelah tabel ini; SQLite mengizinkan
    -- tabel referensi belum ada saat CREATE, baru ditegakkan saat DML).
    session_id        TEXT NOT NULL REFERENCES production_sessions (id) ON DELETE CASCADE,
    number            TEXT NOT NULL,
    client            TEXT NOT NULL,
    shipping_reference TEXT NOT NULL DEFAULT '-',
    target_qty        INTEGER NOT NULL CHECK (target_qty > 0),
    created_at_utc    TEXT NOT NULL,
    UNIQUE (session_id, number)
);

-- Sesi produksi operator. Snapshot kode/nama master disimpan agar perubahan
-- master tidak mengubah konteks transaksi historis.
CREATE TABLE production_sessions (
    id                   TEXT PRIMARY KEY,
    account_id           TEXT NOT NULL REFERENCES accounts (id),
    operator_name        TEXT NOT NULL,
    shift_id             TEXT NOT NULL REFERENCES shifts (id),
    line_id              TEXT NOT NULL REFERENCES lines (id),
    sku_id               TEXT NOT NULL REFERENCES product_skus (id),
    shift_code           TEXT NOT NULL,
    shift_name           TEXT NOT NULL,
    line_code            TEXT NOT NULL,
    line_name            TEXT NOT NULL,
    sku_code             TEXT NOT NULL,
    sku_name             TEXT NOT NULL,
    family_code          TEXT NOT NULL,
    active_work_order_id TEXT,
    context_version      INTEGER NOT NULL DEFAULT 1,
    started_at_utc       TEXT NOT NULL,
    ended_at_utc         TEXT
);
CREATE INDEX idx_production_sessions_active ON production_sessions (account_id, ended_at_utc);
