-- Migrasi 0003: stok label QR internal NG dan job cetak (Fase 2)
-- Entitas: Batch label, Label internal, Job cetak, Detail job.
-- ID label dialokasikan dari sequence DB yang tidak pernah diulang/di-reset.
-- Semua waktu disimpan UTC.

-- Sequence blok per namespace. Alokasi memakai UPDATE sehingga writer yang
-- berjalan paralel saling menunggu dan tidak pernah memperoleh digit yang sama.
CREATE TABLE label_sequences (
    name            TEXT PRIMARY KEY,
    next_value      INTEGER NOT NULL CHECK (next_value > 0),
    updated_at_utc  TEXT NOT NULL
);

-- Basis kosong siap pakai: tanpa baris seed demo, hanya counter alokasi.
INSERT INTO label_sequences (name, next_value, updated_at_utc)
VALUES ('NG', 1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

CREATE TABLE label_batches (
    id                    TEXT PRIMARY KEY,
    namespace             TEXT NOT NULL,
    quantity              INTEGER NOT NULL CHECK (quantity > 0),
    -- Rentang digit yang pernah dialokasikan untuk batch ini (jejak rekonsiliasi).
    first_seq             INTEGER NOT NULL CHECK (first_seq > 0),
    last_seq              INTEGER NOT NULL CHECK (last_seq >= first_seq),
    note                  TEXT NOT NULL DEFAULT '',
    created_by_account_id TEXT NOT NULL REFERENCES accounts (id),
    created_by_name       TEXT NOT NULL,
    created_at_utc        TEXT NOT NULL,
    -- Kunci idempotensi: retry atas permintaan yang sama mengembalikan batch lama.
    idempotency_key       TEXT NOT NULL UNIQUE,
    request_hash          TEXT NOT NULL
);
CREATE INDEX idx_label_batches_created ON label_batches (created_at_utc DESC);

-- Label stok lintas WO. Satu ID hanya boleh terikat pada satu unit seumur hidup (SSOT §4).
CREATE TABLE ng_labels (
    id                TEXT PRIMARY KEY,
    seq               INTEGER NOT NULL UNIQUE,
    namespace         TEXT NOT NULL,
    -- Payload final disimpan apa adanya agar cetak ulang menghasilkan QR yang sama.
    payload           TEXT NOT NULL UNIQUE,
    batch_id          TEXT NOT NULL REFERENCES label_batches (id),
    status            TEXT NOT NULL CHECK (status IN ('AVAILABLE', 'BOUND', 'VOID'))
                          DEFAULT 'AVAILABLE',
    bound_serial      TEXT,
    bound_at_utc      TEXT,
    printed_count     INTEGER NOT NULL DEFAULT 0,
    last_printed_at_utc TEXT,
    created_at_utc    TEXT NOT NULL
);
CREATE INDEX idx_ng_labels_batch ON ng_labels (batch_id, seq);
CREATE INDEX idx_ng_labels_status ON ng_labels (status, created_at_utc);

CREATE TABLE print_jobs (
    id                      TEXT PRIMARY KEY,
    batch_id                TEXT NOT NULL REFERENCES label_batches (id),
    -- FIRST = percobaan pertama, REPRINT = cetak ulang ID yang sama (job baru).
    kind                    TEXT NOT NULL CHECK (kind IN ('FIRST', 'REPRINT')),
    -- Saluran cetak: ekspor CSV NiceLabel (Fase 2) atau printer langsung (Fase 5).
    channel                 TEXT NOT NULL CHECK (channel IN ('NICELABEL_CSV', 'DIRECT_PRINTER')),
    -- EXPORTED = berkas dibuat, hasil fisik belum dikonfirmasi; bukan tanda tercetak.
    status                  TEXT NOT NULL CHECK (status IN ('EXPORTED', 'CONFIRMED', 'UNKNOWN', 'FAILED')),
    quantity                INTEGER NOT NULL CHECK (quantity > 0),
    requested_by_account_id TEXT NOT NULL REFERENCES accounts (id),
    requested_by_name       TEXT NOT NULL,
    requested_at_utc        TEXT NOT NULL,
    confirmed_at_utc        TEXT,
    confirmed_count         INTEGER,
    export_filename         TEXT,
    note                    TEXT NOT NULL DEFAULT '',
    idempotency_key         TEXT UNIQUE
);
CREATE INDEX idx_print_jobs_batch ON print_jobs (batch_id, requested_at_utc DESC);
CREATE INDEX idx_print_jobs_requested ON print_jobs (requested_at_utc DESC);

-- Subset label per job; membuat riwayat cetak ulang dapat ditelusuri per ID.
CREATE TABLE print_job_labels (
    job_id          TEXT NOT NULL REFERENCES print_jobs (id) ON DELETE CASCADE,
    label_id        TEXT NOT NULL REFERENCES ng_labels (id),
    result          TEXT NOT NULL CHECK (result IN ('PENDING', 'PRINTED', 'MISSING'))
                        DEFAULT 'PENDING',
    reported_at_utc TEXT,
    PRIMARY KEY (job_id, label_id)
);
CREATE INDEX idx_print_job_labels_label ON print_job_labels (label_id, job_id);
