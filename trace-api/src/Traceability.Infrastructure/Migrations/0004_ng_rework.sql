-- Migrasi 0004: karantina NG, rework, dan scan OK akhir (Fase 3)
-- Entitas: unit (satu baris per unit fisik), kejadian NG berurutan, catatan rework
-- selesai, hasil OK akhir, dan kunci idempotensi permintaan scan.
-- Semua waktu disimpan UTC. Label internal dari Fase 2 dipakai sebagai identitas
-- karantina ketika serial belum ada atau tidak terbaca (SSOT §5).

-- Unit fisik. Serial boleh NULL (NG sebelum laser) dan boleh terikat satu label
-- internal. Satu label hanya boleh dipakai satu unit seumur hidup (SSOT §4).
CREATE TABLE units (
    id                   TEXT PRIMARY KEY,
    -- Serial disimpan sebagai string agar nol awal tidak hilang (SSOT §6).
    serial               TEXT,
    label_id             TEXT UNIQUE REFERENCES ng_labels (id),
    -- NG_QUARANTINE = Karantina, REWORK_RETEST = Uji ulang, OK_FINAL = OK akhir,
    -- QC_HOLD = Tahan QC (percobaan habis; packing & rework keempat diblokir).
    status               TEXT NOT NULL CHECK (status IN ('NG_QUARANTINE', 'REWORK_RETEST', 'OK_FINAL', 'QC_HOLD')),
    -- Jumlah percobaan rework yang sudah dipakai, total lintas pos/defect/operator/WO.
    attempts             INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    family_code          TEXT NOT NULL DEFAULT '',
    sku_code             TEXT NOT NULL DEFAULT '',
    stage_code           TEXT NOT NULL DEFAULT '',
    -- WO asal diketahui dari kejadian NG pertama; NULL bila tidak diketahui.
    origin_wo_id         TEXT,
    origin_wo_number     TEXT,
    origin_client        TEXT,
    origin_session_id    TEXT,
    -- WO tujuan diisi saat unit lulus OK akhir hasil rework.
    target_wo_id         TEXT,
    target_wo_number     TEXT,
    target_client        TEXT,
    target_session_id    TEXT,
    -- Waktu kejadian NG pertama; NULL untuk unit yang lulus OK tanpa pernah NG.
    first_ng_at_utc      TEXT,
    last_event_at_utc    TEXT NOT NULL,
    ok_at_utc            TEXT,
    ok_actor_name        TEXT,
    created_at_utc       TEXT NOT NULL,
    updated_at_utc       TEXT NOT NULL
);

-- Serial unik bila diketahui; NULL berulang diizinkan (SQLite menganggap NULL berbeda).
CREATE UNIQUE INDEX ux_units_serial ON units (serial) WHERE serial IS NOT NULL;
CREATE INDEX idx_units_status ON units (status, last_event_at_utc DESC);
CREATE INDEX idx_units_target_wo ON units (target_wo_number, last_event_at_utc DESC);

-- Kejadian NG: satu baris per kejadian, berurutan per unit (seq stabil walau
-- timestamp sama). Kejadian dengan defect sama tetap baris terpisah (SSOT §5).
CREATE TABLE unit_ng_events (
    id                   TEXT PRIMARY KEY,
    unit_id              TEXT NOT NULL REFERENCES units (id),
    seq                  INTEGER NOT NULL CHECK (seq > 0),
    label_id             TEXT NOT NULL,
    -- Snapshot master: nama/kode historis tidak berubah saat master diedit (SSOT §2).
    stage_id             TEXT NOT NULL,
    stage_code           TEXT NOT NULL,
    stage_name           TEXT NOT NULL,
    defect_id            TEXT NOT NULL,
    defect_group         TEXT NOT NULL,
    defect_name          TEXT NOT NULL,
    -- Percobaan rework yang sudah dipakai saat kejadian dicatat (0 = NG pertama).
    attempt_no           INTEGER NOT NULL CHECK (attempt_no >= 0),
    -- REWORK_REQUIRED = masih bisa diperbaiki, QC_HOLD = percobaan habis.
    disposition          TEXT NOT NULL CHECK (disposition IN ('REWORK_REQUIRED', 'QC_HOLD')),
    wo_origin_id         TEXT,
    wo_origin_number     TEXT,
    actor_account_id     TEXT NOT NULL,
    actor_name           TEXT NOT NULL,
    shift_code           TEXT NOT NULL DEFAULT '',
    line_code            TEXT NOT NULL DEFAULT '',
    sku_code             TEXT NOT NULL DEFAULT '',
    family_code          TEXT NOT NULL DEFAULT '',
    session_id           TEXT NOT NULL DEFAULT '',
    note                 TEXT NOT NULL DEFAULT '',
    occurred_at_utc      TEXT NOT NULL,
    UNIQUE (unit_id, seq)
);
CREATE INDEX idx_ng_events_unit ON unit_ng_events (unit_id, seq);
CREATE INDEX idx_ng_events_time ON unit_ng_events (occurred_at_utc DESC);

-- Rework selesai: satu baris per percobaan (attempt_no unik per unit). Retry atas
-- permintaan yang sama tidak menambah baris karena diredam kunci idempotensi.
CREATE TABLE unit_rework_events (
    id                   TEXT PRIMARY KEY,
    unit_id              TEXT NOT NULL REFERENCES units (id),
    attempt_no           INTEGER NOT NULL CHECK (attempt_no > 0),
    ng_event_id          TEXT NOT NULL REFERENCES unit_ng_events (id),
    action               TEXT NOT NULL,
    actor_account_id     TEXT NOT NULL,
    actor_name           TEXT NOT NULL,
    shift_code           TEXT NOT NULL DEFAULT '',
    line_code            TEXT NOT NULL DEFAULT '',
    session_id           TEXT NOT NULL DEFAULT '',
    note                 TEXT NOT NULL DEFAULT '',
    performed_at_utc     TEXT NOT NULL,
    UNIQUE (unit_id, attempt_no)
);
CREATE INDEX idx_rework_events_unit ON unit_rework_events (unit_id, attempt_no);

-- OK akhir: tepat satu baris per unit. Serial unik agar satu serial tidak masuk
-- dua kali, dan WO tujuan disimpan terpisah dari WO asal (SSOT §3).
CREATE TABLE unit_ok_scans (
    id                   TEXT PRIMARY KEY,
    unit_id              TEXT NOT NULL UNIQUE REFERENCES units (id),
    serial               TEXT NOT NULL,
    label_id             TEXT,
    -- SERIAL_ONLY = scan biasa, REWORK_PAIR = pasangan label + serial hasil rework.
    mode                 TEXT NOT NULL CHECK (mode IN ('SERIAL_ONLY', 'REWORK_PAIR')),
    wo_target_id         TEXT,
    wo_target_number     TEXT,
    client               TEXT,
    actor_account_id     TEXT NOT NULL,
    actor_name           TEXT NOT NULL,
    shift_code           TEXT NOT NULL DEFAULT '',
    line_code            TEXT NOT NULL DEFAULT '',
    sku_code             TEXT NOT NULL DEFAULT '',
    session_id           TEXT NOT NULL DEFAULT '',
    occurred_at_utc      TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_ok_scans_serial ON unit_ok_scans (serial);

-- Kunci idempotensi permintaan scan: retry mengembalikan hasil yang sama dan tidak
-- menambah record; kunci sama dengan isi berbeda ditolak sebagai konflik.
CREATE TABLE scan_request_keys (
    key                  TEXT PRIMARY KEY,
    operation            TEXT NOT NULL,
    request_hash         TEXT NOT NULL,
    unit_id              TEXT,
    result_ref           TEXT,
    created_at_utc       TEXT NOT NULL
);
CREATE INDEX idx_scan_keys_unit ON scan_request_keys (unit_id);
