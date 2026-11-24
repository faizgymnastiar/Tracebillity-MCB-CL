# API LELCO Traceability v1

Swagger UI menyediakan daftar operasi backend, parameter, dan skema DTO. Panduan ini menjelaskan cara login dan urutan transaksi produksi.

## Menjalankan Swagger di Docker

Dari root repo, gunakan file Compose tambahan untuk menerbitkan API hanya ke loopback laptop.

```powershell
docker compose --env-file .env -f deployment/compose.yaml -f deployment/compose.swagger.yaml up -d --build --no-deps trace-api
```

- [Swagger UI](http://127.0.0.1:5081/swagger/index.html)
- [OpenAPI JSON](http://127.0.0.1:5081/swagger/v1/swagger.json)
- [Health check API](http://127.0.0.1:5081/api/v1/health)

Gunakan konfigurasi environment yang sama dengan stack aktif. Port Swagger bisa diubah lewat `TRACE_SWAGGER_PORT`. Dashboard tetap memakai alamat Docker yang biasa. Sertakan `compose.swagger.yaml` saat membuat ulang backend agar port Swagger tetap tersedia. Konfigurasi Compose standar menjalankan backend dalam mode Production dengan Swagger nonaktif. Saat menjalankan API langsung dalam environment Development, Swagger aktif secara default dan mengikuti port proses API.

Swagger menggunakan rute backend .NET. Frontend Next.js memiliki rute proxy yang menggunakan cookie login browser dan bisa memiliki nama berbeda, misalnya endpoint PNG backend berakhiran `qr.png`.

## Autentikasi

1. Buka **Authorize**. Jika kolom **InternalKey** tersedia, masukkan nilai `TRACE_INTERNAL_API_KEY` dari konfigurasi lokal. Backend mengharapkannya melalui `X-Trace-Internal-Key`. Health check dikecualikan.
2. Jalankan `POST /api/v1/auth/login` dengan email dan password akun. Respons berisi `token`, `expiresAtUtc`, dan `profile`.
3. Buka **Authorize** lagi dan masukkan nilai `token` ke **Session**. Swagger meneruskannya melalui `X-Trace-Session`. Uji lewat `GET /api/v1/auth/me`.
4. Jika profil menunjukkan `mustChangePassword`, gunakan `PUT /api/v1/auth/change-password`. Akhiri pengujian dengan `POST /api/v1/auth/logout`.

Izin akun diperiksa di backend. Daftar izin akun terdapat di `auth/me`, sedangkan katalog izin tersedia di `GET /api/v1/accounts/permissions`. Operasi tulis dari Swagger memakai basis data backend yang sedang berjalan.

## Alur produksi

1. Ambil ID shift, line, dan SKU yang aktif dari `GET /api/v1/master/catalog/active`.
2. Jalankan `POST /api/v1/production/start` dengan `shiftId`, `lineId`, `skuId`, dan `workOrders`. Setiap WO membawa `number`, `client`, `shippingReference`, dan `targetQty`. Gunakan `activeWorkOrderNumber` untuk memilih WO awal.
3. Ambil `production.contextVersion` terbaru dari `GET /api/v1/production/current`. Kirim nilainya melalui `X-Trace-Context-Version` saat mencatat NG, rework, OK akhir, atau packing. Ambil ulang setelah konteks berubah, misalnya setelah mengganti WO aktif.
4. Untuk unit NG, pilih pos dan ID defect dari `GET /api/v1/ng/stations`, lalu panggil `POST /api/v1/ng/records/ng` dengan `label`, `stationCode`, `defectId`, dan `idempotencyKey`. Catat rework lewat `POST /api/v1/ng/records/rework` dengan `unitRef`, `action`, dan `idempotencyKey`.
5. Simpan OK akhir lewat `POST /api/v1/ng/records/ok`. Gunakan `mode: "SERIAL"` dan `serial` untuk unit langsung OK, atau `mode: "REWORK"` dengan `label` dan `serial` untuk unit hasil rework. Sertakan `idempotencyKey`. Pakai kembali key dan payload yang sama saat retry transaksi yang sama. Lepas stiker NG setelah respons berhasil dengan `removeLabel: true`.
6. Packing memakai `POST /api/v1/packing/{kind}/{actionName}`. Pilih `kind` berupa `inner` atau `outer` dan aksi `open`, `add`, atau `close`. Kirim QR box sebagai `qr`. Untuk aksi `add`, kirim serial unit sebagai `member` pada inner, atau QR inner sebagai `member` pada outer.

## Kelompok endpoint

Semua path di bawah relatif terhadap `/api/v1`. Gunakan OpenAPI JSON untuk inventaris metode dan parameter.

| Grup | Kegunaan |
| --- | --- |
| `/health`, `/config` | Status API dan konfigurasi aplikasi. |
| `/auth`, `/accounts` | Login, sesi, profil, dan administrasi akun. |
| `/master/catalog`, `/master/lists` | Katalog keluarga produk, SKU, shift, line, pos QC, dan defect. |
| `/production`, `/session/context` | Sesi produksi, WO aktif, dan konteks operator. |
| `/labels` | Generate batch label NG, QR, histori dan status cetak, ekspor CSV, serta job USB. |
| `/ng` | Validasi jenis hasil scan, karantina, rework, OK akhir, dan riwayat NG. |
| `/packing/history`, `/packing/history/unit?reference=...` | Pencarian unit dan detail relasi serial, label NG, inner, outer, dan WO. |
| `/packing/production-report` | Total produksi, tren, dan kategori NG dengan filter `from`, `to`, `sku`, dan `wo`. |
| `/traceability` | Scan gate lama, daftar record, ringkasan harian, dan ekspor laporan. |

Operasi lama yang membaca JSON secara manual dapat tampil tanpa skema body. Swagger menghasilkan skema dari DTO yang dideklarasikan pada controller. Untuk respons yang dikembalikan sebagai `IActionResult` tanpa tipe eksplisit, gunakan contoh respons aktual dari akun uji.

## Membaca error

| HTTP | Arti umum |
| --- | --- |
| 400 | Input tidak valid. Validasi model ASP.NET dapat mengembalikan objek `errors`. |
| 401 | Token sesi hilang, tidak sah, atau kedaluwarsa, atau kunci internal tidak sesuai. |
| 403 | Izin kurang atau akun perlu mengganti password. |
| 404 | Data atau rute tidak ditemukan. |
| 409 | Konflik status atau konteks. `NO_PRODUCTION_SESSION` berarti sesi produksi belum aktif. `CONTEXT_STALE` berarti versi konteks perlu diperbarui. |

Error bisnis umumnya berisi `error` dan `code`. Gunakan `code` sebagai acuan penanganan di aplikasi.
