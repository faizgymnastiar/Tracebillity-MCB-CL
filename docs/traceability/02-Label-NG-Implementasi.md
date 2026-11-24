# Fase 2 — Hasil Kerja Stok Label QR NG

Dokumen ini melengkapi [02-Label-NG.md](02-Label-NG.md) dan menjadi serah terima implementasi:
**kontrak payload internal**, **format CSV**, **prosedur restore sequence**, **bukti scan balik**,
plus skema, konfigurasi, daftar endpoint, kode kesalahan, dan langkah verifikasi.

Status: implementasi selesai — item terbuka dicatat di §12.
Verifikasi yang sudah dijalankan di mesin pengembangan (build only): `dotnet build Traceability.sln`
(0 warning, 0 error), `npm run typecheck`, `npm run lint`, `npm run build` di `trace-web`.
Mesin serah terima tidak boleh menjalankan binary hasil build lokal (Smart App Control), sehingga
§10 adalah checklist runtime + formulir bukti untuk pihak penerima.

## 1. Peta langkah kerja → implementasi

| Langkah 02-Label-NG.md | Wujud di kode |
|---|---|
| 1. Entitas Batch, Label internal, Job cetak, Detail job | `Traceability.Domain/LabelEntities.cs` (`LabelBatchRecord`, `NgLabelRecord`, `PrintJobRecord`) + tabel di `Migrations/0003_label_stock.sql` |
| 2. Sequence DB tidak diulang, unique terpisah ID & payload, namespace internal, payload final tersimpan | `label_sequences` + `LabelStore.AllocateBlock`, `ng_labels.id PRIMARY KEY` / `ng_labels.payload UNIQUE`, `NgLabelRules` |
| 3. Generate ID: jumlah positif + batas per permintaan, atomik, idempotency key | `LabelStockService.Generate`, `LabelStore.Allocate` (satu transaksi), `label_batches.idempotency_key UNIQUE` + `request_hash` |
| 4. Daftar batch, rincian ID, riwayat cetak (filter tanggal + pagination), ekspor CSV | `GET /labels/batches`, `/labels/batches/{id}`, `/labels/labels`, `/labels/print-jobs`, `/labels/print-jobs/{id}/export.csv` + layar **Generate ID** |
| 5. Operator mencatat hasil cetak; ekspor bukan tanda tercetak; cetak ulang = job baru pada ID sama; label terikat tidak dipakai ulang | `POST /labels/print-jobs/{id}/confirm`, status `EXPORTED→CONFIRMED/UNKNOWN/FAILED`, `kind=FIRST/REPRINT`, `ng_labels.status/bound_serial` |
| 6. Dokumentasi perlindungan alokasi ID saat restore | `LabelIssuanceLedger` (berkas append-only di luar DB) + `POST /labels/sequence/reconcile` + §6 dokumen ini |

## 2. Kontrak payload internal (kontrak scanner)

Dimiliki satu kelas: `Traceability.Domain/NgLabelRules.cs`. Tidak ada tempat lain yang
membentuk/membaca payload.

| Elemen | Nilai | Contoh |
|---|---|---|
| Namespace | `NG` | — |
| Awalan payload QR (dicetak & di-scan) | `NG:` | `NG:000123` |
| Awalan ID label (bentuk manusiawi) | `NG-` | `NG-000123` |
| Digit sequence | minimum 6, melebar sendiri di atas 999.999 | seq `123` → `000123`; seq `12345678` → `12345678` |
| Pola payload (scanner) | `^NG:(\d{6,})$` (case-insensitive) | terima `ng:000123` |
| Pola ID (input manual operator) | `^NG-(\d{6,})$` | `NG-000123` |
| Batas sequence (penjaga overflow) | `1 … 999.999.999.999` | — |
| ID batch | `LB-<firstSeq:D6>` | batch pertama → `LB-000001` |
| ID job cetak | GUID tanpa tanda hubung (32 karakter) | `9f3c…` |

Aturan scanner (SSOT §6) — `QrClassifier.Classify`:

- Payload berawalan `NG:` → `InternalLabel` (satu-satunya tipe yang ditegakkan Fase 2).
- Semua payload lain → `Unknown`. **Panjang string bukan dasar deteksi.** Serial produk,
  inner box, dan outer box baru dikenali lewat lookup register pada Fase 3 karena format
  nyatanya belum dikonfirmasi (SSOT §11).
- Payload yang sudah dicetak **tidak pernah dihitung ulang** dari template: `ng_labels.payload`
  tersimpan permanen, sehingga cetak ulang menghasilkan QR yang identik byte-per-byte.
- Satu ID label hanya boleh terikat satu unit seumur hidup (`status AVAILABLE/BOUND/VOID` +
  `bound_serial`); kolom ini disiapkan Fase 2 dan ditulis oleh Fase 3.

## 3. Format CSV untuk template NiceLabel

Dihasilkan `LabelStockService.BuildCsv`; endpoint `GET /api/v1/labels/print-jobs/{jobId}/export.csv`
(dan field `exportCsv` pada respons `POST /labels/print-jobs`).

Baris header **persis** dan identitas kolom tidak berubah untuk cetak pertama maupun cetak ulang:

```
LABEL_ID,PAYLOAD,BATCH_ID,SEQ,NAMESPACE,LABEL_STATUS,BATCH_QTY,GENERATED_AT_UTC,GENERATED_BY,PRINT_JOB_ID,PRINT_KIND,PRINT_CHANNEL
```

Contoh isi (2 baris data):

```
"NG-000123","NG:000123","LB-000123",123,"NG","AVAILABLE",2,"2026-11-19T02:10:00.0000000+00:00","Rega Ariyanto","9f3c1b2e4d5a46f78b0c1d2e3f405a6b","FIRST","NICELABEL_CSV"
"NG-000124","NG:000124","LB-000123",124,"NG","AVAILABLE",2,"2026-11-19T02:10:00.0000000+00:00","Rega Ariyanto","9f3c1b2e4d5a46f78b0c1d2e3f405a6b","FIRST","NICELABEL_CSV"
```

Kesepakatan teknis:

| Aspek | Aturan |
|---|---|
| Encoding | UTF-8 **tanpa BOM** |
| Akhir baris | CRLF (`\r\n`) eksplisit, tidak mengikuti OS server |
| Quote | Semua sel teks ber-quote; tanda `"` digandakan |
| Nama berkas | `NG-labels_{batchId}_{kind}_{yyyyMMdd-HHmmss WIB}.csv` — diturunkan dari job, jadi unduh ulang menghasilkan nama & isi yang sama |
| Kolom yang dipakai template | `PAYLOAD` → komponen QR; `LABEL_ID` → teks manusia di bawah QR |
| Kolom lain | Informasi jejak audit; **boleh dibaca, tidak boleh dijadikan kunci data master** |
| Perubahan skema | Kolom lama tidak pernah diganti nama/dihapus; kolom baru hanya ditambahkan di ujung |
| Batas baris per berkas | Sama dengan batas job cetak (`Trace:Labels:MaxPerRequest`) |

## 4. Skema baru (migrasi 0003)

| Tabel | Isi penting | Penegakan |
|---|---|---|
| `label_sequences` | `name` (satu baris `NG`), `next_value`, `updated_at_utc` | `PRIMARY KEY (name)`, `CHECK (next_value > 0)` |
| `label_batches` | `id`, `namespace`, `quantity`, `first_seq`, `last_seq`, `note`, pembuat + waktu, `idempotency_key`, `request_hash` | `quantity > 0`; `last_seq >= first_seq`; **`idempotency_key UNIQUE`**; FK `accounts(id)` |
| `ng_labels` | `id`, `seq`, `namespace`, `payload`, `batch_id`, `status`, `bound_serial`, `bound_at_utc`, `printed_count`, `last_printed_at_utc`, `created_at_utc` | **`id PRIMARY KEY`** dan **`seq UNIQUE`** dan **`payload UNIQUE`** (unique terpisah untuk ID & payload sesuai langkah 2); `status IN (AVAILABLE, BOUND, VOID)`; FK batch |
| `print_jobs` | `id`, `batch_id`, `kind`, `channel`, `status`, `quantity`, operator + waktu, `confirmed_at_utc`, `confirmed_count`, `export_filename`, `note`, `idempotency_key` | `kind IN (FIRST, REPRINT)`, `channel IN (NICELABEL_CSV, DIRECT_PRINTER)`, `status IN (EXPORTED, CONFIRMED, UNKNOWN, FAILED)`, `quantity > 0`, `idempotency_key UNIQUE` |
| `print_job_labels` | `job_id`, `label_id`, `result`, `reported_at_utc` | `PRIMARY KEY (job_id, label_id)` — subset label per job; `result IN (PENDING, PRINTED, MISSING)`; `ON DELETE CASCADE` |

Indeks: `idx_label_batches_created`, `idx_ng_labels_batch`, `idx_ng_labels_status`,
`idx_print_jobs_batch`, `idx_print_jobs_requested`, `idx_print_job_labels_label`.
Semua waktu disimpan UTC; penyajian memakai WIB.

## 5. Alokasi ID: paralel, atomik, idempoten

1. **Blok digit dari sequence DB.** Dalam satu transaksi:
   `UPDATE label_sequences SET next_value = next_value + @qty` lalu dibaca balik
   `next_value - @qty`. SQLite menahan write-lock sejak statement pertama sehingga dua
   generator paralel saling menunggu dan **tidak pernah menerima digit yang sama**.
2. **Penyangga sebelum commit.** `SELECT COUNT(1) FROM ng_labels WHERE seq BETWEEN first AND last`
   harus 0. Bila tidak, transaksi dibatalkan dan service menaikkan lantai sequence lalu
   membalas `409 SEQUENCE_REUSE` (permintaan berikutnya tinggal diulang).
3. **Satu transaksi untuk batch + seluruh label.** Kegagalan di tengah (proses mati, DB busy,
   ledger tidak bisa ditulis) → rollback total; **tidak ada batch parsial**. Nomor yang sudah
   pernah teralokasi tidak dipakai ulang, jadi **celah sequence diperbolehkan** dan bukan cacat.
4. **Idempotensi.** `request_hash = SHA-256("NG|{quantity}|{note}")` (hex huruf kecil) disimpan
   bersama `idempotency_key UNIQUE`. Key sama + hash sama → batch lama dikembalikan
   (`reused: true`, HTTP 200). Key sama + hash beda → `409 IDEMPOTENCY_CONFLICT`.
5. **Batas terukur per permintaan.** `quantity` wajib > 0 dan ≤ `Trace:Labels:MaxPerRequest`
   (default 500). Batas ini hanya membatasi **satu permintaan**, tidak membatasi jumlah label
   sepanjang umur sistem — `label_sequences` tidak pernah di-reset.

## 6. Prosedur restore sequence (perlindungan alokasi ID)

### 6.1 Catatan penerbitan (ledger)

`LabelIssuanceLedger` menulis satu baris **sebelum transaksi DB commit** ke berkas append-only
yang berada **di luar file basis data**:

```
{waktuUtc}|{batchId}|{firstSeq}|{lastSeq}|{pelaku}
2026-11-19T02:10:00.0000000+00:00|LB-000123|123|124|Rega Ariyanto
2026-11-20T03:41:12.0000000+00:00|RECONCILE|0|1840|Rega Ariyanto — pemulihan backup 15-11
```

Lokasi: `Trace:Labels:LedgerPath`; default = `label-issuance.log` di samping file DB.
Di container compose path dipisah ke volume `trace-label-ledger` (`/var/lib/trace-labels`)
agar backup DB yang tertinggal **tidak ikut membawa ledger lama**.

Gagal menulis ledger = batch dibatalkan seluruhnya (`409 LEDGER_WRITE_FAILED`).

### 6.2 Aturan konsistensi

```
highWater  = max(next_value - 1, MAX(ng_labels.seq), ledgerMaxSeq)
consistent = ledgerMaxSeq <= next_value - 1 AND maxLabelSeq <= next_value - 1
```

Saat start, API mencatat: `Sequence label NG: next_value X, tertinggi diketahui Y, ledger <path>`.
Bila tidak konsisten, log peringatan menyebut `POST /api/v1/labels/sequence/reconcile` dan
**semua generate ditolak dengan `409 SEQUENCE_LAG`** sampai rekonsiliasi selesai — sistem tidak
akan menerbitkan ulang ID yang mungkin masih beredar.

### 6.3 Langkah pemulihan backup yang tertinggal

1. Hentikan `trace-api`.
2. Kembalikan `traceability.db` (+ `-wal`, `-shm`) dari backup ke volume `trace-data`.
   **Jangan** menyentuh volume `trace-label-ledger`.
3. Start `trace-api`. Baca log start atau `GET /api/v1/labels/sequence`.
4. Bila `consistent = false`: kumpulkan batas tertinggi dari catatan penerbitan yang masih utuh —
   ledger (`ledgerMaxSeq`), berkas CSV job yang pernah dicetak (`PRINT_JOB_ID`/`LABEL_ID`),
   atau label fisik yang masih beredar di lantai (scan → `GET /api/v1/labels/resolve`).
5. Jalankan rekonsiliasi (hak `print.manage`; `$base` dan `$s` seperti contoh di §10):
   ```powershell
   Invoke-RestMethod -Method Post -Uri "$base/api/v1/labels/sequence/reconcile" -WebSession $s `
     -ContentType 'application/json' `
     -Body '{"observedMaxSeq": 1840, "note": "laporan stok 20-11-2026"}'
   ```
   `observedMaxSeq` boleh dikosongkan bila ledger utuh — lantai diambil dari `max(maxLabelSeq, ledgerMaxSeq)`.
   Nilai dilaporkan ≤ lantai yang sudah dikenal → tidak ada yang dinaikkan, keputusan tetap dicatat
   sebagai baris `RECONCILE`.
6. Pastikan `GET /api/v1/labels/sequence` → `consistent: true`, lalu generate berjalan lagi.
7. Bila ledger **dan** DB sama-sama hilang: pulihkan dari berkas CSV terakhir yang tercetak /
   hasil sortir label fisik, laporkan nilai tertingginya, dan catat dasar perhitungannya di `note`.

Batas atas proteksi tetap `ng_labels.seq`/ledger; sistem **tidak pernah** menurunkan
`next_value`, hanya menaikkan.

## 7. Konfigurasi

| Variable (.NET / env) | Default | Fungsi |
|---|---|---|
| `Trace__Labels__MaxPerRequest` | `500` | Batas jumlah label per permintaan generate **dan** per job cetak |
| `Trace__Labels__LedgerPath` | `<folder DB>/label-issuance.log` | Lokasi catatan penerbitan; isi path di luar folder DB untuk pemisahan backup (compose: `/var/lib/trace-labels/label-issuance.log`) |

Env compose (lihat `deployment/.env.example`): `TRACE_LABEL_MAX_PER_REQUEST`,
`TRACE_LABEL_LEDGER_PATH`. Panjang maksimal: catatan 200 karakter, kunci idempotensi 120 karakter.

## 8. Endpoint baru (`/api/v1/labels`, prefix web sama)

| Method | Path | Izin | Keluaran utama |
|---|---|---|---|
| GET | `/labels` | `print.manage` \| `history.read` | Ringkasan stok + kondisi sequence |
| GET | `/labels/sequence` | `print.manage` \| `history.read` | `nextValue`, `maxLabelSeq`, `ledgerMaxSeq`, `highWaterSeq`, `consistent`, `ledgerPath` |
| POST | `/labels/sequence/reconcile` | `print.manage` | Kondisi sequence setelah lantai dinaikkan |
| GET | `/labels/batches?search&from&to&page&pageSize` | `print.manage` \| `history.read` | Halaman batch + jumlah per status (`pageSize` ≤ 200) |
| POST | `/labels/batches` | `print.manage` | Body `{ quantity, note, idempotencyKey }` → batch + label + sequence |
| GET | `/labels/batches/{batchId}?labelLimit` | `print.manage` \| `history.read` | Rincian batch: label (`0` = semua, maks 2000) + 100 job terakhir |
| GET | `/labels/labels?batchId&status&page&pageSize` | `print.manage` \| `history.read` | Halaman label (`pageSize` ≤ 500) |
| GET | `/labels/resolve?value` | `print.manage` \| `history.read` | Label hasil scan (payload atau ID) |
| GET | `/labels/print-jobs?batchId&status&from&to&page&pageSize` | `print.manage` \| `history.read` | Halaman riwayat cetak |
| POST | `/labels/print-jobs` | `print.manage` | Body `{ batchId, labelIds[], channel, note, idempotencyKey }` → job + CSV |
| GET | `/labels/print-jobs/{jobId}` | `print.manage` \| `history.read` | Satu job + subset label |
| GET | `/labels/print-jobs/{jobId}/export.csv` | `print.manage` | Berkas CSV (`content-disposition: attachment`) |
| POST | `/labels/print-jobs/{jobId}/confirm` | `print.manage` | Body `{ status, printedCount, note, results[] }` → job terkonfirmasi |

Filter `from`/`to` menerima tanggal operasi **WIB** (`yyyy-MM-dd`) dan dikonversi ke rentang UTC
setengah-terbuka (`Wib.TryWibDayRangeUtc`) agar tetap memakai indeks.

Job cetak: `labelIds` kosong = seluruh batch; seluruh label harus satu batch (`400 LABEL_SCOPE`);
`kind` ditentukan otomatis — bila sebagian label pernah masuk job sebelumnya maka `REPRINT`.
`idempotencyKey` opsional; bila dipakai dan sudah ada jobnya, job lama dikembalikan tanpa berkas baru.

Konfirmasi hasil cetak:

- `EXPORTED` (status awal) **tidak** menghitung label sebagai tercetak.
- `CONFIRMED` tanpa `results` → seluruh label job menjadi `PRINTED`; `printed_count` label +1 dan
  `last_printed_at_utc` diisi. `results` berisi `MISSING` hanya menurunkan jumlah, tidak menambah.
- `printedCount` yang dikirim harus sama dengan jumlah `PRINTED` (`400 PRINT_COUNT`).
- `UNKNOWN` / `FAILED` → status job tercatat, tidak ada label yang dianggap tercetak, dan `results`
  ditolak (`400 PRINT_RESULT`).
- Job yang sudah `CONFIRMED` → `409 PRINT_ALREADY_CONFIRMED`; buat job `REPRINT` baru pada ID sama.

### 8.1 Kode kesalahan baru

| HTTP | `code` | Artinya |
|---|---|---|
| 403 | `FORBIDDEN` | Akun tidak punya `print.manage` untuk tindakan tulis |
| 404 | `NOT_FOUND` | Batch / job / label tidak dikenal |
| 400 | `LABEL_QTY` | Jumlah kosong atau ≤ 0 |
| 400 | `LABEL_QTY_LIMIT` | Jumlah melebihi `Trace:Labels:MaxPerRequest` |
| 400 | `LABEL_IDEMPOTENCY_KEY` | Kunci idempotensi kosong / > 120 karakter |
| 400 | `LABEL_BATCH_REQUIRED` / `LABEL_UNKNOWN` / `LABEL_SCOPE` / `LABEL_EMPTY_BATCH` | Batch tidak dipilih, label tidak ada, campur batch, batch kosong |
| 400 | `PRINT_QTY_LIMIT` | Job cetak melebihi batas per permintaan |
| 400 | `PRINT_STATUS` / `PRINT_RESULT` / `PRINT_COUNT` | Status konfirmasi tidak sah, hasil per label salah, jumlah tidak cocok |
| 400 | `LABEL_STATUS` / `PRINT_CHANNEL` | Filter/nilai status atau saluran tidak dikenal |
| 400 | `LABEL_SCAN` / `QR_NOT_INTERNAL` | Payload scan kosong / bukan label internal NG |
| 400 | `SEQUENCE_OBSERVED` | `observedMaxSeq` ≤ 0 |
| 409 | `IDEMPOTENCY_CONFLICT` | Key sama dipakai untuk isi berbeda |
| 409 | `SEQUENCE_LAG` | Generate berhenti sampai rekonsiliasi (restore tertinggal) |
| 409 | `SEQUENCE_REUSE` | Digit blok ternyata sudah pernah terbit — lantai dinaikkan otomatis, ulangi permintaan |
| 409 | `DB_BUSY` | Transaksi SQLite saling bertabrakan; ulangi |
| 409 | `LEDGER_WRITE_FAILED` | Catatan penerbitan tidak dapat ditulis; batch dibatalkan seluruhnya |
| 409 | `PRINT_ALREADY_CONFIRMED` | Job sudah dikonfirmasi; gunakan cetak ulang |

## 9. Perubahan trace-web

- Route handler proxy baru di `src/app/api/v1/labels/**` (11 berkas) — tanpa logika bisnis.
- `src/components/traceability/LabelStockWorkspace.tsx`: layar **Generate ID** dengan tab
  *Generate ID* · *Batch & stok* · *Cetak & riwayat* · *Verifikasi scan*; filter tanggal WIB,
  pagination server, tabel batch/label/job, ekspor & unduh ulang CSV, form catatan hasil cetak,
  panel sequence + rekonsiliasi, dan pencocokan payload hasil scan.
- Kunci idempotensi dibuat per percobaan (`GEN-<uuid>`) dan **tidak** dirotasi bila permintaan
  adalah kirim ulang (`reused`), sehingga respons yang putus lalu dikirim ulang mengembalikan
  batch yang sama.
- Menu navigasi `traceability → Generate ID` (`icon: print`) dan slug baru
  `/modules/traceability/labels`.
- `src/server/session.ts`: hak backend `print.manage` dipetakan ke izin UI
  `traceability.label` **terpisah** dari `traceability.write`, jadi akun khusus cetak membuka
  layar label tanpa membuka layar scan (dan sebaliknya). Pembacaan daftar juga diterima
  `traceability.history`.
- `src/types/index.ts`: tipe `NgLabelInfo`, `LabelBatchInfo`, `PrintJobInfo`, `LabelBatchDetail`,
  `LabelSequenceState`, `LabelStockSummary`, `GenerateBatchResult`, `PrintJobResult`.

## 10. Bukti scan balik label contoh (diisi penerima saat uji terima)

Uji HTTP pada stack Docker terisolasi lulus untuk penerbitan dua payload berbeda,
retry generate, konflik key dengan jumlah berbeda, ekspor CSV sesuai subset, konfirmasi
job dengan data sintetis, job reprint terpisah, dan resolusi kembali payload.
Penerimaan fisik tetap mensyaratkan cetak melalui NiceLabel dan scan pada perangkat operator.

Prasyarat: API + web berjalan, akun uji punya hak `print.manage`.

```powershell
$base = 'http://127.0.0.1:3001'          # atau http://<server>:8080 lewat nginx
# Login lewat trace-web: token disimpan di cookie httpOnly, jadi pakai -SessionVariable.
Invoke-WebRequest -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' `
  -Body '{"email":"...","password":"..."}' -SessionVariable s | Out-Null

# 1. Terbitkan batch contoh 2 label
$g = Invoke-RestMethod -Method Post -Uri "$base/api/v1/labels/batches" -WebSession $s `
  -ContentType 'application/json' `
  -Body '{"quantity":2,"note":"uji terima fase 2","idempotencyKey":"UJI-F2-0001"}'
$g.batch.id; $g.labels | Select-Object id, payload

# 2. Buat job cetak lalu unduh berkas CSV dari endpoint ekspor
$j = Invoke-RestMethod -Method Post -Uri "$base/api/v1/labels/print-jobs" -WebSession $s `
  -ContentType 'application/json' -Body ('{"batchId":"' + $g.batch.id + '"}')
Invoke-WebRequest -Uri "$base/api/v1/labels/print-jobs/$($j.job.id)/export" -WebSession $s `
  -OutFile $j.exportFilename
# cetak berkas itu lewat template NiceLabel yang memakai kolom PAYLOAD

# 3. Scan label hasil cetak, lalu cocokkan isinya dengan record di basis data
$value = [uri]::EscapeDataString($g.labels[0].payload)
Invoke-RestMethod -Uri "$base/api/v1/labels/resolve?value=$value" -WebSession $s
```

Bila memanggil trace-api langsung (mis. `http://127.0.0.1:5080`), ganti cookie dengan header
`x-trace-session` dari respons `POST /auth/login` dan tambahkan `x-trace-internal-key` bila
`Trace:InternalApiKey` terisi.

Formulir bukti (lampirkan hasil cetak + tangkapan layar respons):

| Field | Nilai |
|---|---|
| Batch diuji | `LB-…` |
| ID label contoh | `NG-………` |
| Payload hasil scan scanner | `NG:………` |
| Payload di `GET /labels/resolve` | harus **sama persis** |
| ID terbaca sebagai label internal | Ya / Tidak |
| Nama berkas CSV | `NG-labels_…_first_…csv` |
| Jumlah baris CSV = jumlah label job | Ya / Tidak |
| Status job sebelum & sesudah dicatat | `EXPORTED` → `CONFIRMED` |
| `printedCount` label sebelum / sesudah | 0 → 1 |
| Operator & tanggal uji | |

## 11. Uji penerimaan — pemenuhan dan cara membuktikan

| Kasus (02-Label-NG.md) | Dijamin oleh | Cara menguji |
|---|---|---|
| Generate paralel → semua ID & payload berbeda | Alokasi blok `UPDATE` + write-lock SQLite; `seq UNIQUE`, `payload UNIQUE` | Jalankan 5 request `POST /labels/batches` serentak (key berbeda) → `SELECT COUNT(DISTINCT id), COUNT(*) FROM ng_labels` sama |
| Respons putus lalu dikirim ulang → batch sama | `idempotency_key UNIQUE` + `request_hash`; UI tidak rotasi key saat kirim ulang | `POST` dengan key yang sama dua kali → respons kedua `reused: true`, `batch.id` identik |
| Gagal di tengah transaksi → tanpa batch parsial; celah boleh ada | Satu transaksi + ledger sebelum commit; blok digit tidak ditarik kembali | Hentikan API saat generate besar / penuhi disk ledger → `label_batches` tidak punya baris separuh; `next_value` boleh melompat |
| Ekspor lalu scan dari NiceLabel → payload sama & terbaca sebagai ID internal | Payload permanen + `NgLabelRules` + `GET /labels/resolve` | §10 |
| Cetak ulang sebagian batch → job baru, jumlah & ID tepat | `labelIds` subset, `kind=REPRINT`, `print_job_labels` | Centang 3 label di rincian batch → buat job → riwayat menunjukkan job `REPRINT` quantity 3 dengan ID yang sama |
| Pengguna tanpa hak generate/cetak → API menolak | `RequireCaller(PrintManage)` di setiap aksi tulis | Akun tanpa `print.manage` → `POST /labels/batches` = 403 `FORBIDDEN`; layarnya read-only |
| Setelah restart / simulasi pemulihan → ID pernah dipakai tidak terbit ulang | Sequence DB persisten + ledger + `SEQUENCE_LAG` + `SEQUENCE_REUSE` | Restart API → `next_value` tidak berubah. Simulasi: pulihkan DB lama tanpa ledger → generate 409 `SEQUENCE_LAG` sampai reconcile |

## 12. Item terbuka & serah terima ke Fase 3

- **Pengikatan label ke unit belum ada** — Fase 2 hanya menyiapkan `status`, `bound_serial`,
  `bound_at_utc`. Fase 3 wajib menulis `AVAILABLE → BOUND` dan menolak label `BOUND` untuk unit lain.
- Status `VOID` tersedia di skema tetapi belum ada aksi koreksinya (SSOT §6 belum menetapkan
  prosedur pembatalan label) — sengaja tidak dibuka agar tidak jadi jalan pintas tanpa jejak.
- Klasifikasi QR lain (serial/inner/outer) menunggu format nyata (SSOT §11); `QrClassifier`
  mengembalikan `Unknown` dan Fase 3 harus menyelesaikannya lewat lookup register, bukan tebakan.
- Saluran `DIRECT_PRINTER` sudah ada di CHECK constraint tetapi belum dipakai — cetak langsung
  baru di Fase 5 setelah uji fisik printer.
- Batas `pageSize` daftar: batch/jobs 200, label 500; `labelLimit` rincian batch maksimal 2000.
- Data transaksi scan (unit/NG/box) masih engine in-memory; stok label sudah persisten di DB.
- Ledger default berada di folder yang sama dengan DB untuk kemudahan lokal; di produksi
  **pindahkan** lewat `Trace__Labels__LedgerPath` (compose sudah memisahkannya) dan pastikan
  volume ledger ikut dibackup **lebih sering** daripada backup DB.
