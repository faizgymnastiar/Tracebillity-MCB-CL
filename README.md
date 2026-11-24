# LELCO Traceability MCB CL

Sistem traceability produksi MCB CL (Plant Jatake) dengan **frontend Next.js** dan **backend .NET 8**
yang dipisah tegas. Mockup HTML asli disimpan di `reference/mockup/` sebagai acuan SSOT visual.

## Struktur repositori

```
.
├── .github/workflows/deploy.yml      CI: build .NET + Next.js, publish image, deploy opsional
├── deployment/
│   ├── compose.yaml                  trace-api + trace-web + nginx (satu port publik + volume DB)
│   ├── .env.example                  variable fondasi (DB, enforcement, admin awal)
│   └── nginx-industrial.conf         Reverse proxy on-prem
├── docs/traceability/                Dokumen per fase + catatan implementasi serah terima
├── reference/mockup/                 Mockup HTML/JS/CSS asli (read-only acuan)
├── trace-api/                        Backend .NET 8 (layered)
│   ├── src/Traceability.Domain/      Entity, aturan barcode, format kode, jam WIB, katalog hak
│   ├── src/Traceability.Application/ Kontrak DTO + interface service
│   ├── src/Traceability.Infrastructure/
│   │   ├── Foundation/               TraceDb (SQLite), migrasi, akun/login, master, sesi produksi
│   │   └── Migrations/*.sql          Skrip SQL terversi (embedded, idempoten)
│   ├── src/Traceability.Api/         Controller HTTP di /api/v1 + penegakan izin
│   ├── Dockerfile
│   └── Traceability.sln
├── trace-web/                        Frontend Next.js 16 (App Router)
│   ├── src/app/                      Halaman + route handler /api/v1 (proxy tipis)
│   ├── src/components/               Workspace scan, label, produksi, database, laporan, super admin
│   ├── src/server/trace-backend.ts   Klien server-side ke trace-api (token + versi konteks)
│   ├── src/server/session.ts         Cookie token sesi + pemetaan hak ke menu UI
│   └── Dockerfile
├── .gitignore
└── README.md
```

## Pembagian tanggung jawab

| Aspek | trace-web (Next.js) | trace-api (.NET 8) |
| --- | --- | --- |
| UI & routing halaman | Ya | Tidak |
| Menyimpan token sesi di cookie `httpOnly` | Ya | Tidak |
| Verifikasi login, hak operasi, masa berlaku sesi | Tidak | Ya |
| Kontrak `/api/v1/**` untuk browser | Proxy tipis | Pemilik data & aturan |
| Scan OK/NG/rework, inner/outer box | - | Ya |
| Rekap harian, database trace, export CSV | - | Ya |
| Akun, hak, master produksi, sesi produksi + WO | Meneruskan | Menyimpan (SQLite) |
| Stok label QR NG, batch, job cetak, sequence penerbitan | Meneruskan | Menyimpan (SQLite) + ledger berkas |
| Konfigurasi brand/kode/printer | Meneruskan | Menyimpan |

Login memakai **email + password** terhadap backend. Cookie `trace_session` (httpOnly) berisi token
opaque yang dikirim ke API sebagai `x-trace-session`; identitas pelaku, hak, dan konteks produksi
selalu diambil backend dari token tersebut, bukan dari input browser.

Header konteks lama masih dikenali **hanya** saat `Trace:Auth:Enforced=false` (mode demo):

```
X-Trace-Operator, X-Trace-Work-Order, X-Trace-Material-Lot, X-Trace-Line,
X-Trace-Shift, X-Trace-Sku, X-Trace-Destination, X-Trace-Admin: granted
```

Transaksi scan juga mengirim `x-trace-context-version` (versi konteks yang sedang tampil di layar).
Bila WO aktif di sesi berubah, versi naik dan backend membalas `409 CONTEXT_STALE` sampai layar
dimuat ulang — hasil scan tidak mungkin masuk ke WO yang salah.

`X-Trace-Internal-Key` opsional; bila `Trace:InternalApiKey` diisi, API menolak request tanpa kunci
(kecuali `/api/v1/health`).

## Menjalankan lokal

Untuk mengatur akses, buka **Super Admin Studio → Akun & izin** dengan akun
yang mempunyai izin kelola akun. Pilih **Preset role** saat membuat atau
mengubah akun, lalu sesuaikan izin tambahan bila diperlukan. Beranda dan
navigasi mengikuti izin tersimpan. Perubahan berlaku saat halaman dimuat ulang.

### Mode development Docker dengan hot reload

Dari root repo, jalankan `docker compose -f .\deployment\compose.dev.yaml up -d`
dan buka [http://127.0.0.1:8000](http://127.0.0.1:8000). Build image aplikasi
tidak diperlukan. Next.js memakai Fast Refresh dan API memakai `dotnet watch`.
Database dan stok QR dev tersimpan di volume tersendiri.

Login pertama memakai `dev-admin@example.test`. Kata sandi sementara tersedia
di log API pada startup pertama. Gunakan `docker compose -f .\deployment\compose.dev.yaml logs trace-api`
untuk membacanya sendiri, lalu ganti kata sandi setelah login. Untuk uji scan OK
di dev, gunakan serial contoh `DEV-SN-001`.

Jika `package-lock.json` berubah, restart layanan web agar `npm ci` dijalankan
ulang. Jika konfigurasi Compose berubah, jalankan `up -d` lagi. Untuk menghentikan
mode dev, jalankan `docker compose -f .\deployment\compose.dev.yaml stop`.

Prasyarat: .NET SDK 8, Node.js 22+, npm.

```powershell
# Terminal 1 — backend (http://127.0.0.1:5080)
dotnet run --project trace-api/src/Traceability.Api/Traceability.Api.csproj

# Terminal 2 — frontend (http://127.0.0.1:3001)
cd trace-web
npm install
npm run dev
```

Saat start pertama, backend membuat basis data SQLite di `data/traceability.db` relatif content root
(folder proyek saat `dotnet run`, `/app/data` di container) lalu menjalankan migrasi terversi dan
mengisi **admin awal** karena tabel akun masih kosong:

- Set `Trace__BootstrapAdmin__Email` (wajib) sebelum start pertama — tanpa email, provisioning
  dilewati dan tidak ada akun yang bisa login (dilihat di log API).
- `Trace__BootstrapAdmin__Password` boleh diisi; bila kosong, backend men-generate password
  sementara yang dicetak SEKALI di log dan wajib diganti pada login pertama (layar login memaksa).

Tidak ada password bawaan yang dikodekan di sumber maupun basis data. Password disimpan sebagai
hash oleh framework (`IPasswordHasher` / PBKDF2 Identity).

`trace-web/.env.local` (opsional):

```
TRACE_API_ORIGIN=http://127.0.0.1:5080
TRACE_API_INTERNAL_KEY=
```

Skrip frontend: `npm run dev`, `npm run build`, `npm run start`, `npm run lint`, `npm run typecheck`.

## Endpoint backend (`trace-api`, prefix `/api/v1`)

| Method | Path | Keterangan |
| --- | --- | --- |
| GET | `/health` | Status store + jalur basis data + jumlah baris |
| POST | `/auth/login` | Login email/password → `{ token, expiresAtUtc, profile }` (token disimpan trace-web sebagai cookie `httpOnly`) |
| POST | `/auth/logout` | Cabut sesi token yang dipakai |
| GET | `/auth/me` | Profil + hak + konteks produksi pemilik token |
| PUT | `/auth/change-password` | Ganti password sendiri (paksa saat login pertama) |
| GET | `/auth/sessions` | Sesi aktif — admin melihat semua, selain itu hanya miliknya |
| POST | `/auth/sessions/{id}/revoke` | Hentikan sesi dari server |
| POST | `/auth/verify-pin` | PIN studio konfigurasi (warisan; saat enforcement tetap butuh login + hak master/akun) |
| GET | `/accounts` / `/accounts/permissions` | Daftar akun & katalog hak (`admin.accounts`) |
| POST/PUT | `/accounts` | Buat akun, ubah nama/status/hak, reset password (`admin.accounts`) |
| GET | `/master/catalog` · `/master/catalog/active` | Katalog master penuh (`admin.master`) / pilihan aktif |
| POST | `/master/catalog/{family\|sku\|shift\|line\|stage\|defect}` | Tambah/ubah master (`admin.master`) |
| PUT | `/master/catalog/{entity}/{id}/status` | Nonaktifkan/aktifkan master (`admin.master`) |
| GET | `/production/current` | Sesi produksi aktif + daftar WO + versi konteks |
| POST | `/production/start` | Mulai sesi (shift/line/SKU + banyak WO + WO aktif) |
| PUT | `/production/active-work-order` | Pindah WO aktif — versi konteks naik |
| POST | `/production/work-orders` | Tambah WO ke sesi berjalan |
| DELETE | `/production/end` | Akhiri sesi produksi |
| GET | `/config`, `/config/public` | Konfigurasi publik tanpa `adminPin` |
| PUT | `/config` | Patch konfigurasi (`admin.master`); kunci `skus`/`lines`/`shifts` ditolak |
| POST | `/config/reset` | Reset konfigurasi default (butuh admin) |
| GET/POST/DELETE | `/master/lists` | Master teks lama: lot, operator, tujuan, tindakan rework |
| GET/PUT | `/session/context` | Konteks default mode demo; ditolak saat enforcement aktif |
| GET/POST/DELETE | `/traceability/scan-state` | Snapshot scan, aksi print/clear/removeItem |
| POST | `/traceability/scans` | Scan OK / NG / REWORK (hak `scan.unit` / `ng.record`) |
| GET | `/traceability/records` | Database trace (`history.read`) |
| GET | `/traceability/records/{id}` | Detail unit / inner box / outer box |
| GET | `/traceability/daily-summary` | Rekap harian + First Pass Yield |
| GET | `/traceability/home-stats` | Metrik hero halaman publik |
| GET | `/traceability/bridge` | Payload printer (BARTENDER_CSV/JSON_API/ZPL_RAW/TSPL_RAW) |
| GET | `/traceability/export` | Unduh CSV (BOM UTF-8) |
| GET | `/labels` · `/labels/sequence` | Ringkasan stok label NG + kondisi sequence penerbitan |
| POST | `/labels/batches` | Generate batch ID label (`print.manage`; idempoten lewat `idempotencyKey`) |
| GET | `/labels/batches` · `/labels/batches/{id}` · `/labels/labels` | Daftar batch, rincian ID, label (filter tanggal WIB + pagination) |
| POST | `/labels/print-jobs` | Job cetak FIRST/REPRINT subset atau satu batch → CSV NiceLabel |
| GET | `/labels/print-jobs` · `/labels/print-jobs/{id}` · `/export.csv` | Riwayat cetak + unduhan CSV kontrak kolom tetap |
| POST | `/labels/print-jobs/{id}/confirm` | Operator mencatat hasil cetak fisik (ekspor saja tidak menghitung tercetak) |
| GET | `/labels/resolve?value` | Payload/ID hasil scan → record label (bukti cetak balik) |
| POST | `/labels/sequence/reconcile` | Pulihkan lantai sequence setelah backup tertinggal (prosedur restore) |
| GET | `/ng/summary` | Ringkasan karantina, Uji ulang, Tahan QC, dan OK akhir hari ini (WIB) + hak pelaku |
| GET | `/ng/stations` | Pos QC + defect aktif keluarga SKU sesi pelaku (dropdown server-side) |
| GET | `/ng/units` · `/ng/units/{unitRef}` | Daftar unit + riwayat lengkap satu unit (`id`/serial/label internal) |
| POST | `/ng/records/ng` | NG pertama mengikat label internal → Karantina; NG ulang hanya dari Uji ulang (`ng.record`) |
| POST | `/ng/records/rework` | Perbaikan selesai: +1 percobaan, kembali ke Uji ulang (`ng.record`) |
| POST | `/ng/records/ok` | OK akhir mode serial saja atau pasangan label + serial hasil rework (`scan.unit`) |

Katalog hak operasi (`admin.accounts` menyerahkannya ke UI):
`scan.unit`, `scan.inner`, `scan.outer`, `ng.record`, `print.manage`, `history.read`,
`admin.master`, `admin.accounts`. Menu di `trace-web` ditampilkan berdasarkan hak yang dikembalikan
`/auth/me`, dan backend tetap menolak walau UI dimanipulasi. Hak `print.manage` (Generate & Cetak
Label) dipetakan ke izin UI `traceability.label` — terpisah dari `traceability.write` (scan) sehingga
akun bagian cetak tidak membuka layar scan.

## Basis data & persistensi

Fase 1 memindahkan akun, hak, master produksi, sesi produksi, dan work order ke **SQLite**
(`Trace:Db:Path`, default `data/traceability.db`). Skema dibuat oleh migrasi SQL terversi yang
ditanam di aset assembly (`Traceability.Infrastructure/Migrations/*.sql`) dan dicatat di tabel
`schema_versions`; migrasi berjalan otomatis saat start dan **idempoten** — dijalankan ulang tidak
menghasilkan duplikasi data demo.

Engine scan lama (unit, inner/outer box, riwayat) masih berada di store in-memory dan dibangun
kembali dari seed saat proses start. Mengganti provider cukup di `ServiceRegistration.cs`
(`TraceDb`/`ITraceStore`) tanpa mengubah controller maupun frontend.

Fase 2 menambahkan stok label QR internal NG di basis data yang sama (migrasi `0003_label_stock.sql`):
`label_sequences` (alokasi ID tidak pernah diulang), `label_batches`, `ng_labels` (ID & payload unik,
status `AVAILABLE/BOUND/VOID`), `print_jobs`, dan `print_job_labels`. Selain itu ada **catatan
penerbitan (ledger)** berupa berkas append-only di luar file DB — default `label-issuance.log` di
samping DB, di container dipisah ke volume sendiri. Ledger inilah yang menghentikan generate
(`409 SEQUENCE_LAG`) ketika backup yang dipulihkan tertinggal dari label yang masih beredar;
pulihkan dengan `POST /api/v1/labels/sequence/reconcile`. Lihat
[docs/traceability/02-Label-NG-Implementasi.md](docs/traceability/02-Label-NG-Implementasi.md) §6.

Fase 3 menambahkan alur karantina di migrasi `0004_ng_rework.sql`: `units` (status
`NG_QUARANTINE/REWORK_RETEST/OK_FINAL/QC_HOLD`, hitungan `attempts`, WO asal & tujuan),
`unit_ng_events` (satu baris per kejadian, `UNIQUE(unit_id, seq)`), `unit_rework_events`
(`UNIQUE(unit_id, attempt_no)`), `unit_ok_scans` (satu hasil OK per unit, serial unik), dan
`scan_request_keys` (kunci idempotensi). Setiap operasi tulis berjalan dalam satu transaksi
yang diawali penulisan kunci idempotensi; transisi status dijaga `UPDATE … WHERE status = …`
plus constraint unik, sehingga dua operator tidak dapat memindahkan unit yang sama dua kali.
Batas percobaan per unit diatur `Trace:Ng:MaxAttempts` (default 3). Rancangan transisi,
contoh request/response, dan bukti histori serial ada di
[docs/traceability/03-NG-Rework-Implementasi.md](docs/traceability/03-NG-Rework-Implementasi.md).

## Deployment

```powershell
cd deployment
copy .env.example .env    # isi TRACE_INTERNAL_API_KEY + kredensial admin awal
docker compose up -d --build
```

- `nginx` → `http://<server>:8080` (port publik, ubah lewat `TRACE_PUBLIC_PORT`).
- `trace-web:3000` dan `trace-api:8080` hanya di network internal compose.
- Health check publik: `GET http://<server>:8080/healthz`.
- Basis data di volume named `trace-data` (`/app/data` di container) — tetap ada saat image diganti.
- Catatan penerbitan label di volume named `trace-label-ledger` (`/var/lib/trace-labels`) — **jangan**
  ikut dipulihkan saat mengembalikan backup DB yang lebih tua.

Variable fondasi (lihat `deployment/.env.example`): `TRACE_DB_PATH`, `TRACE_AUTH_ENFORCED`,
`TRACE_SESSION_LIFETIME_HOURS`, `TRACE_BOOTSTRAP_EMAIL`, `TRACE_BOOTSTRAP_PASSWORD`,
`TRACE_LABEL_MAX_PER_REQUEST`, `TRACE_LABEL_LEDGER_PATH`, `TRACE_NG_MAX_ATTEMPTS`.

### Backup & pulih

```powershell
# Backup (hentikan writer sebentar agar berkas WAL konsisten, atau checkpoint dulu)
docker compose stop trace-api
docker cp lelco-traceability-trace-api-1:/app/data ./backup-data
docker cp lelco-traceability-trace-api-1:/var/lib/trace-labels ./backup-ledger
docker compose start trace-api

# Pulih — HANYA basis data; ledger penerbitan tidak boleh ikut kembali ke masa lalu
docker compose stop trace-api
docker cp ./backup-data/. lelco-traceability-trace-api-1:/app/data
docker compose start trace-api
# lalu baca GET /api/v1/labels/sequence; bila consistent=false jalankan sequence/reconcile
```

Membawa serta `*.db`, `*.db-wal`, `*.db-shm` sekaligus. Restore ke instance baru cukup menyalin
berkas lalu start — migrasi yang sudah tercatat di `schema_versions` dilewati, migrasi baru
dijalankan otomatis. Backup ledger (berkas `label-issuance.log`) dianjurkan lebih sering daripada
backup DB karena berkas itu penjaga agar ID lama tidak pernah terbit ulang.

Workflow `.github/workflows/deploy.yml` menjalankan build .NET + lint/typecheck/build Next.js,
menerbitkan image `ghcr.io/<repo>/trace-api` dan `trace-web`, lalu (bila `DEPLOY_ENABLED=true`)
menyalin `deployment/` ke server pabrik dan `docker compose pull && up -d`.

Secret/variable yang dipakai workflow: `DEPLOY_SSH_KEY`, `DEPLOY_HOST`, `DEPLOY_USER` (secrets) dan
`DEPLOY_PATH`, `DEPLOY_ENABLED` (variables).
