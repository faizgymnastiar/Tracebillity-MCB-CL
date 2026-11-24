# Fase 1 — Hasil Kerja Fondasi Produksi

Dokumen ini melengkapi [01-Fondasi.md](01-Fondasi.md) dan menjadi serah terima
implementasi: inventarisasi data, keputusan skema, konfigurasi, prosedur
backup/migrasi/pemulihan, daftar migrasi, dan langkah verifikasi.

Status: implementasi selesai — sisa item terbuka dicatat di §6.
Verifikasi yang sudah dijalankan di mesin pengembangan: `dotnet build Traceability.sln`
(0 warning, 0 error), `npm run typecheck`, `npm run lint`, `npm run build` di `trace-web`.
Verifikasi runtime (menjalankan API + browser) mengikuti daftar langkah di §7.

## 1. Inventarisasi instance, seed, dan data lama (Langkah 1)

### 1.1 Instance penyimpanan sebelum Fase 1

| Komponen | Sifat | Lokasi | Bertahan restart? |
|---|---|---|---|
| `InMemoryTraceStore` (trace-api) | Singleton in-memory per proses API | RAM proses | Tidak — seluruh data hilang saat restart |
| Cookie `trace_operator` (trace-web) | Konteks login basi-64, tanpa token server | Browser | Tidak relevan server |
| Cookie `trace_admin` (trace-web) | Penanda PIN `granted` | Browser | Tidak relevan server |
| `AppConfig` (DefaultConfig) | Konfigurasi + master list di dalam store in-memory | RAM proses | Tidak |

Kesimpulan penting: **belum ada data persisten sama sekali**. Tidak ada database
lama yang perlu dimigrasikan atau dibersihkan, sehingga tidak ada perubahan
destruktif terhadap data produksi. Ini memenuhi syarat "jika klasifikasi data
belum jelas, hentikan penghapusan" — tidak ada yang dihapus.

### 1.2 Klasifikasi isi seed & master saat ini

| Data | Sumber | Klasifikasi | Perlakuan Fase 1 |
|---|---|---|---|
| 4 hari riwayat produksi (unit `SN…`, inner/outer box, NG, rework, tiket `WO-2026-01…04`) | `SeedData.cs` (dibangun ulang setiap start) | **Dummy** (fixture uji tampilan) | Dibiarkan sebagai sample-data non-produksi di engine in-memory; tidak ikut ke DB baru. Tidak dihapus karena tidak persisten. |
| Operator `OP-2026-REGA/TAOFIQ/OWEN/IHSAN` | `DefaultConfig.cs` | **Dummy** | **Tidak dimigrasi.** Akun login hanya dibuat lewat provisioning/admin; tidak ada akun demo di migrasi. |
| SKU `MCB CL P1/P3 - x A` (14 varian: P1 2/4/6/10/16/20/25/35/50 A + P3 10/16/20/25/35 A) | `DefaultConfig.OfficialSkus` | **Master riil** (keluarga MCB CL, varian SKU sesuai SSOT §2/§3) | Dijadikan seed master DB (keluarga + SKU) karena merupakan data referensi sah dari SSOT. |
| Shift 1–3, LINE 01/02 - MCB CL | `DefaultConfig.cs` | **Fixture awal** (contoh yang masih masuk akal; dapat diubah Admin) | Masuk seed DB sebagai baris master biasa; Admin dapat menambah/menonaktifkan. |
| Tujuan `CUST-01…04 - PLN …` | `DefaultConfig.cs` | **Dummy contoh** — format klien/pengiriman belum dikonfirmasi (SSOT §11) | Tidak dijadikan master DB Fase 1. Isi per-WO memakai field klien/pengiriman bebas yang diisi operator di modal Mulai produksi. |
| Lot material `LOT-…` | `DefaultConfig.cs` | **Dummy contoh** | Tetap milik engine lama di konfigurasi; bukan tabel master Fase 1. |
| Daftar rework `RW-01…05` | `DefaultConfig.cs` | **Fixture** — rework sah mengikuti defect pos (Fase 3) | Tetap di konfigurasi lama; master defect DB memakai tabel flowchart SSOT. |
| Pos/defect NG `ST-01…09` + defect | `DefaultConfig.BuildStations()` | **Fixture mendekati riil** tetapi nama tidak identik dengan tabel flowchart SSOT §2 | Diganti dengan seed dari **tabel flowchart SSOT** (nama defect per SSOT, tiap opsi punya ID sendiri walau kode DF berulang). |
| PIN studio lama | `DefaultConfig.cs` | Data warisan mode demo | Studio produksi memakai hak `admin.master` dari akun yang login. |

## 2. Keputusan skema Fase 1

- Penyimpanan relasional persisten: **SQLite** (file, WAL) lewat `Microsoft.Data.Sqlite`,
  migrasi skema SQL terversi tertanam yang dijalankan otomatis saat API start.
  Abstraksi koneksi menyisakan satu titik (`TraceDb`) untuk pindah ke PostgreSQL/MySQL
  tanpa mengubah service.
- Waktu disimpan UTC; tampilan tetap Asia/Jakarta (WIB) di lapisan penyajian.
- Tidak ada akun demo/password bawaan di migrasi. Master yang di-seed hanya data
  referensi SSOT (keluarga, SKU, shift, line, tahap, defect) dan idempoten —
  dijalankan ulang tidak menghasilkan duplikasi (`INSERT OR IGNORE` + constraint unik).
- Sesi login dipegang **backend** (tabel `auth_sessions`, token acak, hash disimpan,
  dapat diakhiri/di-revoke, kedaluwarsa). Cookie web hanya menyimpan token opaque.
- Nomor WO dijamin unik **per sesi produksi** (scope identitas global WO masih
  menunggu konfirmasi pemilik proses — SSOT §11 / bagian Kontrak Fase 1).
- Target per WO: bilangan pcs **positif** (CHECK > 0 di DB dan divalidasi service).
- Field pengiriman: teks bebas per WO sampai contoh nyata dikonfirmasi (SSOT §11).

## 3. Konfigurasi via environment (trace-api)

| Variable .NET (env) | Default | Fungsi |
|---|---|---|
| `Trace__Db__Provider` | `sqlite` | Provider relasional (satu-satunya yang didukung saat ini; validasi menolak lainnya) |
| `Trace__Db__Path` | `data/traceability.db` | Path file DB (relatif content root, atau absolut) |
| `Trace__Auth__Enforced` | `true` | Paksa sesi login + izin di semua endpoint operasi; `false` hanya untuk demo lokal |
| `Trace__BootstrapAdmin__Email` | — | Email admin pertama — **wajib diisi** agar provisioning berjalan (hanya saat tabel akun kosong) |
| `Trace__BootstrapAdmin__Password` | — | Password admin pertama; bila dikosongkan dibuat password sementara yang **diprint sekali ke log** dan wajib diganti setelah login pertama |
| `Trace__Session__LifetimeHours` | `12` | Umur sesi login (selaras cookie `maxAge` web) |

## 4. Prosedur menerapkan migrasi, backup, dan pemulihan

### 4.1 Menerapkan migrasi
- Otomatis: setiap start `Traceability.Api` menjalankan `MigrationRunner`. Skrip
  `.sql` tertanam dieksekusi berurutan; versi yang sudah tercatat di tabel
  `schema_versions` dilewati. Aman dijalankan ulang; database kosong langsung
  terbentuk lengkap (skema + master SSOT, tanpa akun demo).
- Verifikasi: `GET /api/v1/health` → `dataSource` menyebut `sqlite-foundation`,
  dan `Traceability.Api` log startup mencetak daftar versi yang diterapkan/dilewati.

### 4.2 Backup
- Docker (compose): DB berada di named volume `trace-data` (mount `/app/data`).
  Backup: `docker compose stop trace-api` → `docker volume backup` sesuai pola host,
  atau salin seluruh isi volume (`traceability.db`, `-wal`, `-shm`).
- Lokal/manual: salin file `data/traceability.db*` saat API berhenti, atau gunakan
  `sqlite3 traceability.db ".backup cadangan.db"` (mode WAL: file `-wal` ikut).
- Frekuensi anjuran pabrik: harian sebelum shift 1, dan sebelum setiap upgrade image.

### 4.3 Pemulihan
1. Hentikan `trace-api`.
2. Kembalikan file backup ke direktori data (nama sesuai `Trace__Db__Path`).
3. Jalankan ulang API — migrasi otomatis melewati versi yang sudah ada di DB hasil pulih.
4. Cek `GET /api/v1/health` dan login akun admin.

### 4.4 Perubahan destruktif
Fase 1 **tidak melakukan penghapusan data**. Bila di kemudian hari engine scan
dipindah ke DB yang sama, klasifikasi §1.2 menjadi acuan: hanya data berlabel
dummy yang boleh ditinggalkan/dihapus setelah verifikasi dua orang.

## 5. Daftar migrasi

| Versi | File | Isi |
|---|---|---|
| 0001 | `Migrations/0001_foundation.sql` | Tabel fondasi: `accounts`, `account_permissions`, `auth_sessions`, `product_families`, `product_skus`, `shifts`, `lines`, `stages`, `defects`, `production_sessions`, `work_orders` + indeks + CHECK |
| 0002 | `Migrations/0002_master_seed.sql` | Seed master dari SSOT §2/§3: keluarga MCB CL, 14 SKU, Shift 1–3, LINE 01/02, tahap ST-01…ST-09 berurutan, 13 defect per pos dengan ID terpisah (kode DF kembar dipisahkan per baris) |

## 6. Item terbuka (belum boleh dipakai sebagai aturan produksi)

- Scope identitas nomor WO lintas proses/sistem — menunggu contoh nomor WO riil.
- Definisi final field pengiriman — menunggu contoh nyata (SSOT §11).
- Studio konfigurasi memerlukan sesi login dengan hak `admin.master`.
  Saat enforcement aktif, endpoint PIN lama dinonaktifkan. Akun dengan sandi
  sementara harus mengganti sandi sebelum boleh memakai operasi berizin.
- Master teks lama (`materials`, `operators`, `destinations`, `reworks`) masih berada di
  konfigurasi engine lama; `skus`, `lines`, `shifts` sudah dipindah ke basis data dan
  ditolak bila dikirim lewat `PUT /api/v1/config` (`code=MASTER_DB_MANAGED`).
- Data transaksi scan (unit/NG/box) masih engine in-memory sampai Fase 3 memindah
  kejadian ke DB; konteks yang dihasilkan fondasi sudah dipakai oleh transaksi itu.
- Hak `scan.inner` dan `scan.outer` sudah ada di katalog dan cukup untuk membuka layar scan
  (`AnyScanGate`), tetapi belum ada endpoint khusus yang menuntutnya sendiri — pemisahan
  penuh baru dipakai saat fitur label/packing (Fase 2–4) meminta izinnya per aksi.

## 7. Langkah verifikasi (urutan terima-ke-terima)

Build backend dan frontend lulus. Pada stack Docker terisolasi dengan database kosong di
`127.0.0.1:18080`, uji HTTP lulus untuk provisioning dan login, sandi salah, kewajiban
ganti sandi melalui API, pembuatan operator dengan empat izin, penolakan tulis master,
dua WO dalam satu sesi, pergantian WO, penolakan versi stale/hilang, WO duplikat,
target nol, penggantian sesi, persistensi master dan sesi setelah restart, serta logout.
Checklist berikut dapat dipakai untuk uji ulang dan pemeriksaan UI:

1. **Instalasi kosong**: hapus/arsipkan `data/traceability.db` → start API →
   log menunjukkan migrasi 0001 & 0002 diterapkan; `schema_versions` berisi 2 baris.
   Restart API → migrasi dilewati, **tidak ada duplikasi master** (jumlah SKU tetap 14,
   defect tetap 13, `SELECT COUNT(*)` per tabel sama seperti sebelumnya).
2. **Provisioning admin**: set hanya `Trace__BootstrapAdmin__Email` dengan tabel akun kosong → API
   mencetak password sementara sekali di log; login UI memakai email/password itu; ganti password
   setelah login pertama (layar login memaksa). Tanpa email, tidak ada akun dibuat dan log
   memberi tahu bahwa provisioning dilewati.
3. **Login salah / akun nonaktif / sesi berakhir / sesi di-revoke** → ditolak (`401 UNAUTHENTICATED`,
   atau `400 LOGIN_DENIED` untuk sandi salah) di UI dan API.
4. **Operator tanpa izin master** memanggil `POST /api/v1/master/catalog/...` langsung → 403.
5. **Operator dengan 4 hak scan** (scan.unit, scan.inner, scan.outer, ng.record) pada satu akun:
   tab Scan OK (butuh `scan.unit`) dan tab NG/After Rework (butuh `ng.record`) semuanya terbuka;
   akun yang hanya punya `ng.record` hanya melihat tab NG & rework.
6. **Dua WO satu shift**: mulai produksi dengan WO-A & WO-B → ganti WO aktif ke WO-B →
   UI menampilkan WO-B; request scan yang masih membawa konteks WO-A (versi lama) → 409,
   layar memuat ulang konteks sebelum bisa transaksi lagi.
7. **Penolakan backend**: SKU keluarga lain saat sesi, master nonaktif, target 0 → 400.
8. **Restart API/DB**: akun, master, sesi produksi, dan WO tetap ada.
9. **Batas master lama**: `PUT /api/v1/config` yang masih memuat `skus`/`lines`/`shifts` → 400
   `MASTER_DB_MANAGED`; simpan brand/kode/printer tanpa kunci tersebut → berhasil dan proyeksi
   master basis data tetap utuh (`POST /config/reset` juga memasang ulang proyeksi).

## 8. Perubahan API yang diserahkan ke fase berikutnya

### 8.1 Endpoint baru (trace-api, prefix `/api/v1`)

| Method | Path | Izin | Keluaran utama |
|---|---|---|---|
| POST | `/auth/login` | publik | `{ token, expiresAtUtc, profile }` — token dipakai jadi cookie `httpOnly` oleh trace-web |
| POST | `/auth/logout` | login | `{ ok }` — token dicabut dari server |
| GET | `/auth/me` | login | `AuthProfileDto`: `{ accountId, email, fullName, status, mustChangePassword, permissions[], production }` |
| PUT | `/auth/change-password` | login | `{ ok }` |
| GET | `/auth/sessions` | login | daftar sesi (admin melihat semua) |
| POST | `/auth/sessions/{id}/revoke` | login | `{ ok }` |
| GET | `/accounts` | `admin.accounts` | daftar akun + hak + status + wajib ganti sandi |
| GET | `/accounts/permissions` | login | katalog hak + label Indonesia |
| POST | `/accounts` | `admin.accounts` | akun baru (hash password, wajib ganti sandi) |
| PUT | `/accounts` | `admin.accounts` | ubah nama/status/hak atau reset sandi |
| GET | `/master/catalog` | `admin.master` | katalog penuh (termasuk nonaktif) |
| GET | `/master/catalog/active` | login | hanya pilihan aktif untuk modal Mulai produksi |
| POST | `/master/catalog/{family\|sku\|shift\|line\|stage\|defect}` | `admin.master` | katalog terbaru setelah upsert |
| PUT | `/master/catalog/{entity}/{id}/status` | `admin.master` | katalog terbaru setelah aktif/nonaktif |
| GET | `/production/current` | login | `{ active, production }` |
| POST | `/production/start` | hak lantai | `ProductionContextDto` sesi baru |
| PUT | `/production/active-work-order` | hak lantai | `ProductionContextDto` (versi konteks +1) |
| POST | `/production/work-orders` | hak lantai | `ProductionContextDto` |
| DELETE | `/production/end` | hak lantai | `{ ok }` |

### 8.2 Kontrak `ProductionContextDto` (respons `production/*`, disebut `production` di UI)

```json
{
  "productionSessionId": "sesi-...",
  "operatorName": "nama saat login (diambil backend, bukan input browser)",
  "shiftCode": "S1", "shiftName": "Shift 1",
  "lineCode": "L01", "lineName": "LINE 01 - MCB CL",
  "skuCode": "MCB CL P1 - 2 A", "skuName": "MCB CL P1 - 2 A",
  "familyCode": "MCB CL",
  "activeWorkOrderId": "wo-...", "activeWorkOrderNumber": "WO-2026-11",
  "workOrderClient": "PLN UID Jakarta", "shippingReference": "PO-889",
  "targetQty": 5000,
  "contextVersion": 3,
  "startedAtUtc": "2026-11-19T02:10:00.0000000+00:00",
  "workOrders": [
    { "id": "wo-...", "number": "WO-2026-11", "client": "PLN UID Jakarta",
      "shippingReference": "PO-889", "targetQty": 5000 }
  ]
}
```

WO yang sedang aktif ditandai oleh `activeWorkOrderId`/`activeWorkOrderNumber` di atas, bukan oleh
flag per baris.

Nama/kode shift, line, SKU, dan keluarga **disnapshot** ke baris sesi saat Mulai produksi, sehingga
perubahan master nanti tidak mengubah konteks transaksi historis. Snapshot scan
(`GET /traceability/scan-state`) kini membawa `production`, `authEnforced`, dan `permissions`
sehingga UI dapat menyembunyikan tab yang tidak boleh dipakai akun tersebut.

### 8.3 Header & kode kesalahan baru

| Header | Arah | Arti |
|---|---|---|
| `x-trace-session` | web → api | token sesi (dari cookie `httpOnly` `trace_session`) |
| `x-trace-context-version` | web → api | versi konteks yang tampil di layar saat transaksi ditulis |

| HTTP | `code` | Artinya |
|---|---|---|
| 401 | `UNAUTHENTICATED` | token kosong/tidak dikenal, sesi kedaluwarsa, sesi di-revoke, atau akun nonaktif |
| 403 | `FORBIDDEN` | akun tidak punya hak yang dituntut endpoint (pesan menyebut nama haknya) |
| 404 | `NOT_FOUND` | akun/sesi/master yang dirujuk tidak ada |
| 409 | `NO_PRODUCTION_SESSION` | transaksi dikirim tanpa sesi produksi aktif |
| 409 | `CONTEXT_STALE` | layar memakai konteks lama (WO aktif sudah berpindah) — muat ulang |
| 409 | `ACCOUNT_EXISTS` | email akun baru sudah terdaftar |
| 400 | `LOGIN_INPUT` / `LOGIN_DENIED` / `PASSWORD_WRONG` / `PASSWORD_WEAK` | gagal login atau ganti sandi |
| 400 | `SHIFT_INVALID` / `LINE_INVALID` / `SKU_UNKNOWN` / `SKU_INACTIVE` / `SKU_FAMILY` | master nonaktif, tidak ada, atau beda keluarga |
| 400 | `WO_REQUIRED` / `WO_NUMBER` / `WO_CLIENT` / `WO_TARGET` / `WO_ACTIVE` | WO kosong, target ≤ 0, atau WO tidak sah untuk sesi |
| 400 | `MASTER_CODE` / `MASTER_NAME` / `MASTER_ENTITY` / `STAGE_FAMILY` / `DEFECT_STAGE` / `DEFECT_GROUP` | validasi master (kode/nama wajib, relasi sah) |
| 400 | `MASTER_DB_MANAGED` | `skus`/`lines`/`shifts` tidak lagi lewat endpoint konfigurasi lama |
| 400 | `CONTEXT_MANAGED_BY_SESSION` | `PUT /session/context` tidak berlaku saat enforcement aktif |

### 8.4 Perubahan trace-web

- Login email/password + layar paksa ganti password; logout mencabut sesi di server.
- `src/server/session.ts` menyimpan token di cookie `httpOnly` dan memetakan hak backend ke menu UI
  (`traceability.view` / `traceability.write` / `traceability.history` / `gov.admin`).
- Beranda: modal **Mulai produksi** (shift/line/SKU + multi-WO + pilih WO aktif) dan panel sesi aktif.
- Layar scan: strip konteks produksi (WO aktif + versi, klien/tujuan, target), dropdown **Ganti WO**,
  tab dibatasi hak, dan respons `409` ditangani dengan petunjuk muat ulang.
- Super Admin: tab baru **Akun & Izin** (CRUD akun, hak, sesi aktif) dan **Master Produksi**
  (keluarga/SKU/shift/line/tahap/defect + nonaktifkan); tab master lama tidak lagi mengedit
  SKU/line/shift.
