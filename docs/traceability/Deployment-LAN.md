# Menjalankan Traceability di jaringan perusahaan

Untuk uji coba, gunakan Docker yang sudah berjalan di laptop. Untuk operasional tetap, pindahkan stack Docker ke server `192.168.1.198`. Pada kedua cara, komputer operator cukup membuka aplikasi melalui browser.

## Opsi A — Bagikan aplikasi Docker dari laptop Windows

1. Pastikan Docker Desktop berjalan. Di PowerShell, lihat alamat IPv4 adaptor Wi-Fi atau Ethernet yang terhubung ke jaringan operator.

   ```powershell
   ipconfig
   docker ps --filter name=lelco-traceability-nginx --format "table {{.Names}}	{{.Ports}}"
   ```

2. Baca publikasi port Nginx. `0.0.0.0:8080->80/tcp` sudah menerima koneksi di alamat IPv4 laptop. `127.0.0.1:8080->80/tcp` hanya bisa diakses dari laptop. Pada kondisi kedua, buka file `.env` yang dipakai deployment saat ini dan atur `TRACE_PUBLIC_PORT=8080`. Dari root repo, terapkan perubahan hanya ke Nginx.

   ```powershell
   $env:TRACE_PUBLIC_PORT = '8080'
   docker compose --env-file .env -f deployment/compose.yaml up -d --no-deps nginx
   curl.exe -f http://127.0.0.1:8080/healthz
   ```

   Contoh ini memakai `.env` di root repo. Jika konfigurasi aktif ada di `deployment/.env`, gunakan `--env-file deployment/.env`. Jalankan perintah dari folder proyek yang sekarang menjalankan Docker.

3. Pada komputer kedua, ganti contoh IP berikut dengan IPv4 laptop hasil `ipconfig`.

   ```powershell
   Test-NetConnection 192.168.1.50 -Port 8080
   curl.exe -f http://192.168.1.50:8080/healthz
   ```

   Jika berhasil, buka `http://192.168.1.50:8080` di browser dan login dengan akun aplikasi. `192.168.1.198` berlaku setelah stack berjalan di server itu, atau jika alamat tersebut memang milik perangkat yang menjalankan Docker.

4. Jika port gagal dari komputer kedua tetapi health check lokal berhasil, periksa port yang dipublikasikan, profil jaringan Windows, aturan firewall perusahaan, dan apakah Wi-Fi/VLAN mengizinkan komunikasi antarklien. Bila akun Anda berwenang mengatur firewall dan subnet operator sesuai, jalankan contoh berikut di PowerShell Administrator. Sesuaikan subnet dan profil berdasarkan jaringan sebenarnya.

   ```powershell
   Get-NetConnectionProfile
   New-NetFirewallRule -DisplayName 'Traceability LAN TCP 8080' -Direction Inbound `
     -Action Allow -Protocol TCP -LocalPort 8080 -RemoteAddress '192.168.1.0/24' `
     -Profile Domain,Private
   ```

Laptop dan Docker Desktop harus tetap aktif. IP laptop bisa berubah setelah ganti jaringan atau saat DHCP memperbarui alamat. Untuk pengujian LAN gunakan akun uji. Siapkan HTTPS dengan sertifikat perusahaan sebelum mengirim kredensial produksi melalui jaringan.

## Opsi B — Deploy permanen di server 192.168.1.198

Langkah berikut mengasumsikan server Linux Ubuntu/Debian dengan akses SSH, Docker Engine dan Compose v2. Jika host menggunakan Windows Server, gunakan VM Linux dengan IP yang dapat dijangkau PC operator dan jalankan langkah di VM itu. Gunakan IP VM pada pengujian atau arahkan reverse proxy perusahaan ke VM.

### 1. Siapkan host dan source

- Pastikan alamat server benar, port 8080 tersedia, dan jaringan operator memiliki akses ke port aplikasi sesuai kebijakan perusahaan.
- Pasang Docker Engine, Buildx, dan Compose plugin dari repository resmi sesuai OS. Server yang melakukan build perlu akses ke registry Docker, Microsoft Container Registry, NuGet, dan npm. [Ubuntu](https://docs.docker.com/engine/install/ubuntu/) · [Debian](https://docs.docker.com/engine/install/debian/) · [Compose](https://docs.docker.com/compose/install/linux/)
- Salin source versi yang sudah diuji ke server atau clone dari repo internal. Jika perubahan terbaru masih lokal, pastikan versi yang disalin mencakup perubahan tersebut. Konfigurasi rahasia, volume Docker, `node_modules`, `.next`, `bin`, dan `obj` dikelola terpisah.

```bash
ssh akun-deploy@192.168.1.198
cd /path/ke/Tracebility_MCB-CL
ip -br address
docker version
docker compose version
sudo ss -ltn 'sport = :8080'
```

Perintah Docker mengasumsikan akun deploy sudah memiliki akses ke daemon.

### 2. Atur environment produksi

Untuk instalasi baru, buat file `deployment/.env` dengan akses terbatas. Jika sudah ada, edit file yang aktif dan pertahankan rahasia serta lokasi data yang sudah ditetapkan.

```bash
cd deployment
umask 077
cp -n .env.example .env
chmod 600 .env
openssl rand -hex 32
nano .env
```

| Variabel | Nilai atau tindakan |
| --- | --- |
| `TRACE_PUBLIC_PORT` | `192.168.1.198:8080` untuk uji LAN pada interface server itu, atau `127.0.0.1:8080` jika HTTPS berakhir di proxy lokal pada host yang sama. |
| `TRACE_INTERNAL_API_KEY` | Isi hasil `openssl rand -hex 32`. Compose menggunakan nilai yang sama untuk web dan API. |
| `TRACE_AUTH_ENFORCED` | `true`. |
| `TRACE_BOOTSTRAP_EMAIL` | Email admin perusahaan. Hanya digunakan ketika database belum memiliki akun. |
| `TRACE_BOOTSTRAP_PASSWORD` | Isi password awal yang kuat dan unik, atau kosongkan agar server membuat password sementara yang muncul sekali di log. |
| `TRACE_DB_PATH` | `data/traceability.db`, tersimpan di volume `trace-data`. |
| `TRACE_LABEL_LEDGER_PATH` | `/var/lib/trace-labels/label-issuance.log`, tersimpan di volume `trace-label-ledger`. |
| `TRACE_NG_SERIAL_PATTERN` | Isi regex serial yang sudah disetujui dan diuji pada contoh produksi. Salin nilai yang telah bekerja di deployment sebelumnya bila ada. |
| `TRACE_USB_PRINT_ENABLED` | `false` untuk instalasi awal. Aktifkan setelah uji cetak pada PC operator. |

Untuk operasional, gunakan reverse proxy HTTPS perusahaan dan sertifikat yang dipercaya komputer operator. Jika proxy berada di server terpisah, gunakan alamat LAN untuk origin dan batasi akses origin ke proxy sesuai konfigurasi jaringan perusahaan. Pembatasan port Docker perlu diverifikasi dari jaringan klien karena publikasi port dapat melewati aturan UFW biasa. [Docker dan firewall Linux](https://docs.docker.com/engine/network/packet-filtering-firewalls/)

### 3. Jalankan dan uji

Pilih jalur **database baru** atau **pemindahan data laptop** sebelum produksi. Untuk pemindahan, ikuti bagian migrasi di bawah sebelum menyalakan API di server.

```bash
docker compose config --quiet
docker compose up -d --build
docker compose ps
curl -f http://192.168.1.198:8080/healthz
```

Jika origin memakai loopback, uji `http://127.0.0.1:8080/healthz` dari server dan URL HTTPS dari PC operator. Compose standar hanya memublikasikan Nginx. Swagger tetap internal ke konfigurasi pengembangan dan tidak diperlukan untuk operator.

Login dari PC operator, ganti password awal bila diminta, lalu buat akun sesuai izin. Uji satu alur dengan data uji dari sesi produksi dan WO, scan OK dan NG, rework, packing hingga outer, hingga penelusuran serial. Uji cetak menggunakan driver Argox di PC yang terhubung USB dan cocokkan hasil scan label. [Panduan cetak USB](05-Cetak-USB.md)

Sebelum dipakai operasional, cocokkan dashboard produksi dengan riwayat packing dan NG. Kode saat ini masih menginisialisasi data contoh untuk penyimpanan engine lama di memori. Halaman lama seperti Laporan Harian perlu diverifikasi terhadap data produksi. Menghapus volume SQLite tidak menghilangkan seed engine lama.

### 4. Backup dan pemindahan dari laptop

Untuk memindahkan data, akhiri pekerjaan operator dan hentikan `trace-api` di laptop. Salin kedua volume saat penulisan berhenti. Contoh berikut memakai PowerShell dari root repo dengan konfigurasi di `.env`.

```powershell
$backup = Join-Path (Get-Location) ('trace-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
$id = docker compose --env-file .env -f deployment/compose.yaml ps -q trace-api
if (!$id) { throw 'Container trace-api tidak ditemukan' }
docker compose --env-file .env -f deployment/compose.yaml stop trace-api
if ($LASTEXITCODE -ne 0) { throw 'Gagal menghentikan API' }
docker cp "${id}:/app/data" (Join-Path $backup 'data')
if ($LASTEXITCODE -ne 0) { throw 'Gagal menyalin database' }
docker cp "${id}:/var/lib/trace-labels" (Join-Path $backup 'ledger')
if ($LASTEXITCODE -ne 0) { throw 'Gagal menyalin ledger' }
Get-ChildItem $backup -Recurse
```

Transfer folder backup melalui jalur perusahaan yang disetujui. Pertahankan database beserta file WAL/SHM jika ada. Untuk server tujuan yang volumenya masih kosong, dari folder `deployment` buat container tanpa menyalakan API, lalu salin isi folder ke volume.

```bash
backup=/path/ke/trace-backup
docker compose build trace-api
docker compose create trace-api
api_id=$(docker compose ps -aq trace-api)
docker cp "$backup/data/." "$api_id:/app/data/"
docker cp "$backup/ledger/." "$api_id:/var/lib/trace-labels/"
```

Lanjutkan ke langkah **Jalankan dan uji** setelah kedua penyalinan sukses. Akun ikut berpindah bersama database. Verifikasi jumlah data, akun, dan stok label. Pertahankan satu backend aktif yang menerbitkan label NG. Jika memakai backup database lama, simpan ledger terbaru dan rekonsiliasi sequence sebelum generate label lagi. [Prosedur backup dan pulih](../../README.md#backup--pulih)

### 5. Update, backup rutin, dan diagnosis

- Sebelum update, catat versi aplikasi dan backup database serta ledger. Untuk backup di server Linux, gunakan urutan yang sama seperti laptop, yaitu stop API, `docker cp` kedua direktori dari ID container, lalu `docker compose start trace-api` setelah backup selesai. Simpan cadangan di lokasi terpisah dan uji restore pada instance terisolasi.
- Setelah source versi baru tersedia, dari `deployment` jalankan `docker compose up -d --build` dan ulangi health check. Jika proxy mengembalikan 502 setelah frontend dibuat ulang, cek status frontend lalu jalankan `docker compose restart nginx`. Pertahankan volume yang sama karena volume menyimpan akun, transaksi, dan ledger.
- Untuk diagnosis, jalankan `docker compose ps` dan `docker compose logs --tail=100 trace-api trace-web nginx`. Jika API berhenti saat startup, cek konfigurasi bootstrap dan database. Jika login berhasil tetapi OK akhir menolak serial, periksa `TRACE_NG_SERIAL_PATTERN`. Jika hasil health hanya gagal dari PC operator, cek IP, publikasi port, firewall, dan routing VLAN.
