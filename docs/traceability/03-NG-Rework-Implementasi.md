# Fase 3 — Hasil Kerja Karantina, Rework, dan Scan OK Akhir

Dokumen ini melengkapi [03-NG-Rework-OK.md](03-NG-Rework-OK.md) dan menjadi serah terima
implementasi: **tabel transisi aktual**, **contoh request/response tiap operasi**, dan
**bukti histori serial setelah linking**, plus skema, aturan percobaan, kode kesalahan,
pemetaan layar, dan langkah verifikasi.

Status: implementasi selesai — item terbuka dicatat di §12.
Verifikasi yang sudah dijalankan di mesin pengembangan (build only): `dotnet build Traceability.sln`
(0 warning, 0 error), `npm run typecheck`, `npm run lint` (89 berkas), `npm run build` di `trace-web`
semua lolos. Mesin serah terima tidak dapat menjalankan binary hasil build lokal (Smart App
Control), sehingga §7 dan §11 adalah prosedur + formulir bukti untuk dijalankan di server uji.

## 1. Peta langkah kerja → implementasi

| Langkah 03-NG-Rework-OK.md | Wujud di kode |
|---|---|
| 1. NG pertama mengikat label internal yang belum terpakai; unit masuk Karantina | `POST /ng/records/ng` → `NgWorkflowService.RecordNg` + `NgWorkflowStore.RecordFirstNg` (`UPDATE ng_labels … WHERE status='AVAILABLE'`) |
| 2. Rework selesai: percobaan +1, unit ke Uji ulang, kembali ke Assembly | `POST /ng/records/rework` → `NgWorkflowStore.RecordRework` (`attempts = attempts + 1`, status `REWORK_RETEST`) |
| 3. NG ulang setelah rework: baris kejadian baru; Karantina atau Tahan QC bila percobaan habis | `RecordNg` memilih `RecordRepeatNg` ketika label sudah `BOUND`; `NgAttemptRules.StatusAfterNg` |
| 4. OK biasa dengan serial valid | `POST /ng/records/ok` mode `SERIAL` → `RecordOkSerial` (unit baru `OK_FINAL`, tanpa label) |
| 5. OK hasil rework: label + serial + WO tujuan dalam satu transaksi | mode `REWORK` → `RecordOkRework` (serial, status, `ng_labels.bound_serial`, baris OK, kunci idempotensi) |
| 6. Batas percobaan 3 per unit seumur hidup; Tahan QC memblokir packing & percobaan ke-4 | `NgAttemptRules` + `Trace:Ng:MaxAttempts` + `UnitStatus.QcHold` / `UnitStatus.BlocksPacking` |
| 7. Klasifikasi QR di server; tolak QR tertukar, ID palsu, pasangan konflik, WO tujuan = WO asal | `QrClassifier`/`NgLabelRules`, `ValidateSerial`, `PAIR_CONFLICT`, `WO_SAME` |
| 8. Transisi dijaga transaksi + constraint; serial final tidak boleh pindah | `INSERT scan_request_keys` paling awal, `UPDATE … WHERE status = …`, `UNIQUE(unit_id, seq)`, `UNIQUE(unit_id, attempt_no)`, `unit_ok_scans.unit_id UNIQUE` |
| 9. Layar NG (QR internal, satu pos + defect), layar OK (dua mode), instruksi stiker setelah sukses | `trace-web` halaman `quarantine` → `NgReworkWorkspace.tsx` |

Berkas baru Fase 3:

| Lapis | Berkas |
|---|---|
| Migrasi | `trace-api/src/Traceability.Infrastructure/Migrations/0004_ng_rework.sql` |
| Domain | `Traceability.Domain/NgWorkflowEntities.cs` (`UnitStatus`, `NgDisposition`, `OkScanMode`, `NgAttemptRules`, record) |
| Application | `Traceability.Application/NgContracts.cs` (DTO + `INgWorkflowService`) |
| Infrastructure | `Foundation/NgWorkflowStore.cs` (transaksional), `Foundation/NgWorkflowService.cs` (aturan bisnis) |
| Api | `Controllers/NgController.cs` (`/api/v1/ng/**`) |
| Web server | `src/app/api/v1/ng/**` (7 route handler proxy), `src/server/trace-backend.ts` |
| Web UI | `src/components/traceability/NgReworkWorkspace.tsx`, halaman `modules/[group]/[slug]` slug `quarantine`, menu **Karantina & Rework** |

## 2. Skema migrasi 0004

| Tabel | Isi penting | Penegakan |
|---|---|---|
| `units` | Satu baris per unit fisik: `serial` (string, boleh NULL), `label_id`, `status`, `attempts`, `family_code`, `sku_code`, `stage_code`, WO asal (`origin_*`), WO tujuan (`target_*`), `first_ng_at_utc`, `last_event_at_utc`, `ok_at_utc`, `ok_actor_name` | `label_id UNIQUE` (satu label satu unit seumur hidup), `status IN (NG_QUARANTINE, REWORK_RETEST, OK_FINAL, QC_HOLD)`, `attempts >= 0`, **`ux_units_serial` unique parsial `WHERE serial IS NOT NULL`** |
| `unit_ng_events` | Satu baris per kejadian NG: `seq`, snapshot pos (`stage_id/code/name`), snapshot defect (`defect_id/group/name`), `attempt_no`, `disposition`, WO asal, pelaku, shift, line, catatan, waktu | **`UNIQUE (unit_id, seq)`**, `seq > 0`, `disposition IN (REWORK_REQUIRED, QC_HOLD)` |
| `unit_rework_events` | Satu baris per percobaan selesai: `attempt_no`, `ng_event_id`, `action`, pelaku, waktu | **`UNIQUE (unit_id, attempt_no)`**, `attempt_no > 0`, FK ke kejadian NG |
| `unit_ok_scans` | Tepat satu hasil OK per unit: `serial`, `label_id`, `mode`, WO tujuan, klien, pelaku, waktu | **`unit_id UNIQUE`**, **`ux_ok_scans_serial UNIQUE (serial)`**, `mode IN (SERIAL_ONLY, REWORK_PAIR)` |
| `scan_request_keys` | `key` (PK), `operation`, `request_hash`, `unit_id`, `result_ref`, waktu | `PRIMARY KEY (key)` — retry tidak menambah record |

Indeks: `idx_units_status (status, last_event_at_utc DESC)`, `idx_units_target_wo`,
`idx_ng_events_unit`, `idx_ng_events_time`, `idx_rework_events_unit`, `idx_scan_keys_unit`.
Semua waktu disimpan UTC dalam format ISO-8601 round-trip; penyajian memakai WIB.

Catatan desain:

- Serial disimpan sebagai **string** agar nol awal tidak hilang, dan serial tidak wajib
  pada NG pertama (unit bisa NG sebelum laser marking).
- `first_ng_at_utc` **nullable**: unit yang lolos OK tanpa pernah NG tetap punya baris
  `units` (status `OK_FINAL`, `attempts = 0`).
- Snapshot master (nama pos, grup defect, nama defect) disimpan pada kejadian NG sehingga
  pengeditan master tidak mengubah sejarah.

## 3. Tabel transisi aktual

Status unit: `NG_QUARANTINE` (Karantina), `REWORK_RETEST` (Uji ulang), `OK_FINAL`
(OK akhir), `QC_HOLD` (Tahan QC). `n` = `units.attempts` saat operasi dijalankan,
`max` = `Trace:Ng:MaxAttempts` (default 3).

### 3.1 Transisi yang diterima

| # | Operasi | Status awal | Syarat | Status akhir | `attempts` | Baris baru |
|---|---|---|---|---|---|---|
| T1 | `POST /ng/records/ng` — NG pertama | *tidak ada unit* (label `AVAILABLE`) | label internal valid, label belum terikat, pos+defect aktif, sesi produksi | `NG_QUARANTINE` | tetap `0` | `units` + `unit_ng_events` (`seq=1`, `attempt_no=0`, `REWORK_REQUIRED`) |
| T2 | `POST /ng/records/ng` — NG ulang | `REWORK_RETEST` | label/serial milik unit, `n < max` | `NG_QUARANTINE` | tetap `n` | `unit_ng_events` (`seq = max+1`, `attempt_no = n`, `REWORK_REQUIRED`) |
| T3 | `POST /ng/records/ng` — NG ulang terakhir | `REWORK_RETEST` | `n >= max` | `QC_HOLD` | tetap `n` | `unit_ng_events` (`seq = max+1`, `disposition = QC_HOLD`) |
| T4 | `POST /ng/records/rework` | `NG_QUARANTINE` | `n < max`, ada kejadian NG terakhir | `REWORK_RETEST` | `n + 1` | `unit_rework_events` (`attempt_no = n+1`) |
| T5 | `POST /ng/records/ok` mode `SERIAL` | *tidak ada unit* | serial sah (4–64 karakter, bukan QR label, cocok `SnPattern` bila diisi), belum pernah OK, WO aktif tersedia | `OK_FINAL` | `0` | `units` + `unit_ok_scans` (`SERIAL_ONLY`, `label_id` NULL) |
| T6 | `POST /ng/records/ok` mode `REWORK` | `REWORK_RETEST` | label terikat unit itu, `unit.serial` masih NULL, serial baru & belum dipakai, WO tujuan ≠ WO asal | `OK_FINAL` | tetap `n` | `unit_ok_scans` (`REWORK_PAIR`), `units.serial`/`target_wo_*`/`ok_at_utc` terisi, `ng_labels.bound_serial` terisi |

Catatan T6: label **tidak** dilepas di database — label tetap `BOUND` dengan `bound_serial`
sebagai jejak permanen; instruksi "lepas stiker" hanya tindakan fisik di lantai.

### 3.2 Penolakan (tidak ada perubahan state)

| Operasi | Status awal | Kondisi | Kode | HTTP |
|---|---|---|---|---|
| NG | `NG_QUARANTINE` | kejadian sebelumnya belum ditutup rework | `NG_DUPLICATE` | 409 |
| NG | `OK_FINAL` | unit sudah selesai | `UNIT_FINISHED` | 409 |
| NG | `QC_HOLD` | menunggu disposisi QC | `QC_HOLD` | 409 |
| NG (label) | — | label belum terbit / `VOID` / sudah `BOUND` ke unit lain | `LABEL_UNKNOWN` / `LABEL_VOID` / `LABEL_ALREADY_BOUND` | 409 |
| Rework | `NG_QUARANTINE` dengan `n >= max` | percobaan habis (percobaan ke-4) | `QC_HOLD` | 409 |
| Rework | `REWORK_RETEST` | perbaikan sudah tercatat, menunggu OK | `REWORK_ALREADY` | 409 |
| Rework | `OK_FINAL` / `QC_HOLD` | unit selesai / ditahan | `UNIT_FINISHED` / `QC_HOLD` | 409 |
| OK `REWORK` | `NG_QUARANTINE` | rework belum dicatat | `OK_NOT_READY` | 409 |
| OK `REWORK` | label tanpa unit | label belum pernah dipakai | `LABEL_UNBOUND` | 409 |
| OK `REWORK` | `REWORK_RETEST` | `units.serial` sudah terisi | `UNIT_FINISHED` | 409 |
| OK `REWORK` | `REWORK_RETEST` | WO aktif = WO asal unit | `WO_SAME` | 409 |
| OK | — | serial sudah dipakai unit lain / sudah pernah OK | `SERIAL_BOUND_OTHER` / `SERIAL_FINISHED` / `SERIAL_NG_UNIT` | 409 |
| OK | — | serial milik unit NG tetapi mode `SERIAL` | `SERIAL_NG_UNIT` | 409 |
| OK | — | WO aktif belum dipilih pada sesi | `NO_ACTIVE_WO` | 409 |
| Semua | — | dua pekerja bertabrakan pada baris yang sama | `STATE_CHANGED` | 409 |
| Semua | — | layar membawa `x-trace-context-version` lama | `CONTEXT_STALE` | 409 |
| Semua | — | belum ada sesi produksi | `NO_PRODUCTION_SESSION` | 409 |
| Semua | — | key idempotensi dipakai dengan isi berbeda | `IDEMPOTENCY_CONFLICT` | 409 |

## 4. Aturan percobaan (attempt) dan Tahan QC

`NgAttemptRules` (Domain) adalah satu-satunya tempat aturan ini:

| Fungsi | Isi |
|---|---|
| `NormalizeMaxAttempts(x)` | dipakai bila `1 … 10`, selain itu `DefaultMaxAttempts = 3` |
| `CanRecordRework(n, max)` | `n < max` — percobaan keempat tidak pernah bisa dicatat |
| `StatusAfterNg(n, max)` | `n >= max` → `QC_HOLD`, selain itu `NG_QUARANTINE` |
| `DispositionAfterNg(n, max)` | `n >= max` → `QC_HOLD`, selain itu `REWORK_REQUIRED` |

Konsekuensi yang diuji lewat tabel transisi:

1. Percobaan **dihitung saat rework dicatat**, bukan saat NG. NG pertama selalu `attempts = 0`.
2. `attempt_no` pada kejadian NG adalah jumlah percobaan **yang sudah dipakai** saat kejadian
   itu dicatat (0, 1, 2, …), sehingga urutan kejadian dan urutan percobaan bisa direkonstruksi
   tanpa membaca tabel rework.
3. Dengan `max = 3`: rework ke-3 tetap boleh (`attempts` menjadi 3, unit ke `REWORK_RETEST`);
   NG berikutnya langsung `QC_HOLD`; rework ke-4 ditolak `QC_HOLD`.
4. `QC_HOLD` **tidak** bisa keluar lewat API ini — hanya `scan.unit`/`ng.record` yang tidak
   punya operasi pelepasan QC. Disposisi QC (scrap/rework luar) adalah pekerjaan Fase 4/5.
5. Batas berlaku **per unit seumur hidup**, lintas pos, defect, operator, shift, dan WO karena
   hanya ada satu kolom `units.attempts`.

## 5. Kontrak API

Semua endpoint di bawah `/api/v1/ng`. Browser tidak memanggil `trace-api` langsung: route
handler `trace-web` meneruskan cookie sesi (`x-trace-session`) dan header
`x-trace-context-version`. Error selalu berbentuk `{ "error": "...", "code": "..." }`.

| Method | Path | Hak | Keterangan |
|---|---|---|---|
| GET | `/ng/summary` | salah satu: `ng.record`, `scan.unit`, `history.read` | hitungan + 3 antrean (maks 50 per status) + `maxAttempts` + hak pelaku |
| GET | `/ng/stations` | idem | pos QC + defect aktif keluarga SKU pelaku |
| GET | `/ng/units` | idem | `search`, `status`, `page`, `pageSize` (1–200) |
| GET | `/ng/units/{unitRef}` | idem | `unitRef` = ID unit, serial, payload `NG:000123`, atau ID `NG-000123` |
| POST | `/ng/records/ng` | `ng.record` | NG pertama / NG ulang |
| POST | `/ng/records/rework` | `ng.record` | rework selesai |
| POST | `/ng/records/ok` | `scan.unit` | OK akhir mode `SERIAL` / `REWORK` |

### 5.1 `GET /ng/summary`

```json
{
  "counts": {
    "quarantine": 4, "retest": 2, "qcHold": 1,
    "okFinalToday": 27, "ngEventsToday": 9, "reworkToday": 6
  },
  "maxAttempts": 3,
  "permissions": ["scan.unit", "ng.record", "history.read"],
  "quarantine": [
    {
      "id": "a1f0c3d2e4b54c8f9a70b1c2d3e4f506",
      "serial": null,
      "labelId": "NG-000842",
      "labelPayload": "NG:000842",
      "status": "NG_QUARANTINE",
      "statusLabel": "Karantina",
      "attempts": 1,
      "maxAttempts": 3,
      "attemptsRemaining": 2,
      "familyCode": "MCB-CL",
      "skuCode": "MCB-CL-2P-16A",
      "stageCode": "ST-02",
      "originWorkOrderNumber": "WO-2611-A",
      "targetWorkOrderNumber": null,
      "firstNgAtUtc": "2026-11-19T02:12:41.1349821+00:00",
      "lastEventAtUtc": "2026-11-19T03:05:12.5512003+00:00",
      "okAtUtc": null,
      "okActorName": null
    }
  ],
  "retest": [],
  "qcHold": []
}
```

`counts.quarantine/retest/qcHold` adalah stok saat ini; `okFinalToday`, `ngEventsToday`,
`reworkToday` dibatasi hari **WIB** (rentang UTC dihitung `Wib.TryWibDayRangeUtc`).

### 5.2 `GET /ng/stations`

```json
[
  {
    "id": "st-02",
    "code": "ST-02",
    "name": "Uji Kalibrasi",
    "seq": 2,
    "defects": [
      { "id": "df-02-1", "groupCode": "CAL", "name": "Trip tidak putus pada arus uji" },
      { "id": "df-02-2", "groupCode": "CAL", "name": "Waktu trip di luar toleransi" }
    ]
  }
]
```

Dropdown defect **wajib** berasal dari pos yang dipilih; pasangan lain ditolak
`DEFECT_NOT_IN_STATION`.

### 5.3 `POST /ng/records/ng` — NG pertama (mengikat label)

```json
{
  "label": "NG:000842",
  "stationCode": "ST-01",
  "defectId": "df-01-1",
  "note": "Solder dingin pin 2",
  "idempotencyKey": "NG-3f9b1c0e7d2a4a6b9f0e1d2c3b4a5968"
}
```

```json
{
  "unit": {
    "id": "a1f0c3d2e4b54c8f9a70b1c2d3e4f506",
    "serial": null,
    "labelId": "NG-000842",
    "labelPayload": "NG:000842",
    "status": "NG_QUARANTINE",
    "statusLabel": "Karantina",
    "attempts": 0,
    "maxAttempts": 3,
    "attemptsRemaining": 3,
    "familyCode": "MCB-CL",
    "skuCode": "MCB-CL-2P-16A",
    "stageCode": "ST-01",
    "originWorkOrderNumber": "WO-2611-A",
    "targetWorkOrderNumber": null,
    "firstNgAtUtc": "2026-11-19T02:12:41.1349821+00:00",
    "lastEventAtUtc": "2026-11-19T02:12:41.1349821+00:00",
    "okAtUtc": null,
    "okActorName": null
  },
  "event": {
    "id": "6c1d9a0b7e2f4c3d8a5b6c7d8e9f0a1b",
    "seq": 1,
    "labelId": "NG-000842",
    "stageId": "st-01",
    "stageCode": "ST-01",
    "stageName": "Perakitan",
    "defectId": "df-01-1",
    "defectGroup": "SLD",
    "defectName": "Solder dingin",
    "attemptNo": 0,
    "disposition": "REWORK_REQUIRED",
    "workOrderOriginNumber": "WO-2611-A",
    "actorName": "Rega Ariyanto",
    "shiftCode": "S1",
    "lineCode": "LN-01",
    "note": "Solder dingin pin 2",
    "occurredAtUtc": "2026-11-19T02:12:41.1349821+00:00"
  },
  "labelPayload": "NG:000842",
  "reused": false,
  "message": "Unit dikarantina dengan label NG:000842. Tempelkan stiker NG sebelum unit meninggalkan line."
}
```

Penolakan yang sering muncul:

```json
{ "error": "Kolom label hanya menerima QR label internal (NG:000123); QR lain ditolak.", "code": "QR_NOT_INTERNAL" }
{ "error": "Label NG-000842 sudah terikat ke unit lain.", "code": "LABEL_ALREADY_BOUND" }
{ "error": "Defect df-01-2 bukan pilihan aktif pada pos ST-01.", "code": "DEFECT_NOT_IN_STATION" }
```

### 5.4 `POST /ng/records/ng` — NG ulang setelah rework

```json
{
  "label": "NG:000842",
  "stationCode": "ST-02",
  "defectId": "df-02-1",
  "note": "Masih trip di 1,13 In",
  "idempotencyKey": "NG-71c0ab5d3e8f42a1b6c7d8e9f0a1b2c3"
}
```

```json
{
  "unit": { "status": "NG_QUARANTINE", "attempts": 1, "attemptsRemaining": 2, "stageCode": "ST-02" },
  "event": {
    "seq": 2, "stageCode": "ST-02", "defectGroup": "CAL", "defectName": "Trip tidak putus pada arus uji",
    "attemptNo": 1, "disposition": "REWORK_REQUIRED"
  },
  "labelPayload": "NG:000842",
  "reused": false,
  "message": "Kejadian NG ke-2 tercatat. Sisa percobaan 2 dari 3."
}
```

Bila kejadian ini terjadi setelah percobaan terakhir, respons memakai
`"status": "QC_HOLD"`, `"disposition": "QC_HOLD"`, dan pesan
`"… Percobaan habis (3); unit masuk Tahan QC dan menunggu disposisi QC — jangan dilepas ke packing."`

### 5.5 `POST /ng/records/rework`

```json
{
  "unitRef": "NG:000842",
  "action": "RW-02: Penyetelan Ulang Kalibrasi Trip MCB",
  "note": "Trip 1,13 In, verifikasi ulang",
  "idempotencyKey": "RWK-0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e"
}
```

```json
{
  "unit": {
    "status": "REWORK_RETEST", "statusLabel": "Uji ulang",
    "attempts": 1, "maxAttempts": 3, "attemptsRemaining": 2
  },
  "reworkEvent": {
    "id": "b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2",
    "attemptNo": 1,
    "ngEventId": "6c1d9a0b7e2f4c3d8a5b6c7d8e9f0a1b",
    "action": "RW-02: Penyetelan Ulang Kalibrasi Trip MCB",
    "actorName": "Rega Ariyanto",
    "note": "Trip 1,13 In, verifikasi ulang",
    "performedAtUtc": "2026-11-19T02:40:07.8331014+00:00"
  },
  "reused": false,
  "message": "Perbaikan selesai dicatat (percobaan 1 dari 3). Unit kembali ke Uji ulang; lakukan pengujian lalu scan OK."
}
```

`unitRef` menerima payload QR (`NG:000842`), ID label (`NG-000842`), ID unit, atau serial.

### 5.6 `POST /ng/records/ok` — mode serial saja

```json
{
  "mode": "SERIAL",
  "serial": "2405-010001-01",
  "label": "",
  "note": "",
  "idempotencyKey": "OK-1a2b3c4d5e6f708192a3b4c5d6e7f809"
}
```

```json
{
  "unit": {
    "serial": "2405-010001-01", "labelId": null, "labelPayload": null,
    "status": "OK_FINAL", "statusLabel": "OK akhir",
    "attempts": 0, "maxAttempts": 3, "attemptsRemaining": 3,
    "originWorkOrderNumber": null, "targetWorkOrderNumber": "WO-2611-A",
    "firstNgAtUtc": "", "okAtUtc": "2026-11-19T03:20:55.2203418+00:00", "okActorName": "Rega Ariyanto"
  },
  "okScan": {
    "serial": "2405-010001-01", "labelId": null, "mode": "SERIAL_ONLY",
    "workOrderTargetNumber": "WO-2611-A", "client": "PT Contoh Pelanggan",
    "actorName": "Rega Ariyanto", "occurredAtUtc": "2026-11-19T03:20:55.2203418+00:00"
  },
  "reused": false,
  "removeLabel": false,
  "message": "Serial 2405-010001-01 tercatat OK akhir pada WO WO-2611-A."
}
```

Mode `SERIAL` **menolak** isi `label` (`PAIR_CONFLICT`) supaya QR label tidak tertukar kolom.

### 5.7 `POST /ng/records/ok` — mode pasangan label + serial (hasil rework)

```json
{
  "mode": "REWORK",
  "serial": "2405-000842-02",
  "label": "NG:000842",
  "note": "",
  "idempotencyKey": "OK-9f8e7d6c5b4a39281706f5e4d3c2b1a0"
}
```

```json
{
  "unit": {
    "serial": "2405-000842-02", "labelId": "NG-000842", "labelPayload": "NG:000842",
    "status": "OK_FINAL", "statusLabel": "OK akhir",
    "attempts": 3, "maxAttempts": 3, "attemptsRemaining": 0,
    "originWorkOrderNumber": "WO-2611-A", "targetWorkOrderNumber": "WO-2611-B",
    "firstNgAtUtc": "2026-11-19T02:12:41.1349821+00:00",
    "lastEventAtUtc": "2026-11-19T03:22:10.8894412+00:00",
    "okAtUtc": "2026-11-19T03:22:10.8894412+00:00", "okActorName": "Rega Ariyanto"
  },
  "okScan": {
    "serial": "2405-000842-02", "labelId": "NG-000842", "mode": "REWORK_PAIR",
    "workOrderTargetNumber": "WO-2611-B", "client": "PT Contoh Pelanggan B",
    "actorName": "Rega Ariyanto", "occurredAtUtc": "2026-11-19T03:22:10.8894412+00:00"
  },
  "reused": false,
  "removeLabel": true,
  "message": "Unit NG:000842 lulus OK akhir sebagai serial 2405-000842-02 pada WO WO-2611-B. Lepas stiker sementara sekarang."
}
```

`removeLabel = true` hanya pada mode rework dan **hanya** setelah transaksi commit. Layar
menampilkan instruksi melepas stiker setelah respons ini diterima; bila respons tidak pernah
sampai, operator tidak boleh melepas stiker sebelum mengecek riwayat unit.

### 5.8 `GET /ng/units/{unitRef}` — lihat §8

## 6. Idempotensi, retry, dan tabrakan paralel

| Aspek | Aturan |
|---|---|
| Kunci wajib | `idempotencyKey` 8–120 karakter; ditolak `400 IDEMPOTENCY_KEY` bila kosong/terlalu pendek |
| Hash isi | `SHA-256` atas bagian terpangkas yang digabung `U+001F`: NG = `("NG", labelId, stationCode, defectId, note)`; rework = `(unitRef, action, note)`; OK = `(operation, serial, labelId, WO aktif, note)` |
| Retry sah | key sama + hash sama + operasi diizinkan → HTTP 200, `reused: true`, **tidak ada record baru** |
| Retry salah | key sama + hash/operasi beda → `409 IDEMPOTENCY_CONFLICT` |
| NG ulang vs retry | retry memakai key yang sama dan menampilkan pesan "sudah tercatat sebelumnya (kejadian NG ke-n)"; NG baru wajib key baru dan hanya diterima dari status `REWORK_RETEST` |
| Waktu habis (timeout) | muat ulang riwayat unit; bila kejadian sudah ada, jangan simpan ulang dengan key baru |
| Dua operator paralel | statement pertama tiap transaksi tulis adalah `INSERT scan_request_keys`; bila key bentrok → rollback + putar ulang hasil. Key berbeda tetapi objek sama → `UPDATE … WHERE status = …` (rowcount 0) atau constraint unik → `STATE_CHANGED` / `LABEL_ALREADY_BOUND` / `SERIAL_*` |

Layar `trace-web` menyimpan kunci per isi formulir: selama isi tidak berubah, tombol simpan
yang ditekan ulang mengirim **kunci yang sama**; setelah server menjawab sukses, kunci
diganti sehingga pemindaian berikutnya menjadi transaksi baru.

## 7. Bukti histori serial setelah linking

### 7.1 Skenario (unit dengan 3 percobaan lalu OK)

| Waktu WIB | Peristiwa | Invarian yang dibuktikan |
|---|---|---|
| 09:12 | `POST /ng/records/ng` label `NG:000842`, pos `ST-01`, defect `Solder dingin` | `units.attempts = 0`, kejadian `seq=1`, `attempt_no=0` |
| 09:16 | Unit keluar line membawa stiker; status Karantina | `ng_labels.status = BOUND`, `bound_serial` masih NULL |
| 09:40 | `POST /ng/records/rework` (`RW-02`) | `attempts = 1`, baris rework `attempt_no = 1`, status Uji ulang |
| 10:05 | `POST /ng/records/ng` pos `ST-02`, defect `Trip tidak putus…` | kejadian `seq=2`, `attempt_no=1` (defect sama pun tetap baris baru) |
| 10:31 | `POST /ng/records/rework` (`RW-03`) | `attempts = 2`, status Uji ulang |
| 10:52 | `POST /ng/records/ng` pos `ST-01`, defect `Solder dingin` | kejadian `seq=3`, `attempt_no=2` |
| 11:15 | `POST /ng/records/rework` (`RW-05`) | `attempts = 3`, status Uji ulang (**percobaan terakhir**) |
| 11:22 | `POST /ng/records/ok` mode `REWORK`, serial `2405-000842-02`, WO tujuan `WO-2611-B` | `units.status = OK_FINAL`, `units.serial` terisi, `unit_ok_scans` satu baris `REWORK_PAIR`, `ng_labels.bound_serial = 2405-000842-02` |

### 7.2 Bentuk bukti dari API

`GET /api/v1/ng/units/NG:000842` (atau `.../2405-000842-02`) mengembalikan **seluruh** jejak
dalam satu respons:

```json
{
  "unit": {
    "id": "a1f0c3d2e4b54c8f9a70b1c2d3e4f506",
    "serial": "2405-000842-02",
    "labelId": "NG-000842",
    "labelPayload": "NG:000842",
    "status": "OK_FINAL",
    "statusLabel": "OK akhir",
    "attempts": 3,
    "maxAttempts": 3,
    "attemptsRemaining": 0,
    "familyCode": "MCB-CL",
    "skuCode": "MCB-CL-2P-16A",
    "stageCode": "ST-01",
    "originWorkOrderNumber": "WO-2611-A",
    "targetWorkOrderNumber": "WO-2611-B",
    "firstNgAtUtc": "2026-11-19T02:12:41.1349821+00:00",
    "lastEventAtUtc": "2026-11-19T03:22:10.8894412+00:00",
    "okAtUtc": "2026-11-19T03:22:10.8894412+00:00",
    "okActorName": "Rega Ariyanto"
  },
  "ngEvents": [
    {
      "seq": 1, "labelId": "NG-000842", "stageCode": "ST-01", "stageName": "Perakitan",
      "defectGroup": "SLD", "defectName": "Solder dingin", "attemptNo": 0,
      "disposition": "REWORK_REQUIRED", "workOrderOriginNumber": "WO-2611-A",
      "actorName": "Rega Ariyanto", "shiftCode": "S1", "lineCode": "LN-01",
      "note": "Solder dingin pin 2", "occurredAtUtc": "2026-11-19T02:12:41.1349821+00:00"
    },
    {
      "seq": 2, "labelId": "NG-000842", "stageCode": "ST-02", "stageName": "Uji Kalibrasi",
      "defectGroup": "CAL", "defectName": "Trip tidak putus pada arus uji", "attemptNo": 1,
      "disposition": "REWORK_REQUIRED", "workOrderOriginNumber": "WO-2611-A",
      "actorName": "Rega Ariyanto", "shiftCode": "S1", "lineCode": "LN-01",
      "note": "Masih trip di 1,13 In", "occurredAtUtc": "2026-11-19T03:05:12.5512003+00:00"
    },
    {
      "seq": 3, "labelId": "NG-000842", "stageCode": "ST-01", "stageName": "Perakitan",
      "defectGroup": "SLD", "defectName": "Solder dingin", "attemptNo": 2,
      "disposition": "REWORK_REQUIRED", "workOrderOriginNumber": "WO-2611-A",
      "actorName": "Rega Ariyanto", "shiftCode": "S1", "lineCode": "LN-01",
      "note": "", "occurredAtUtc": "2026-11-19T03:52:40.1170031+00:00"
    }
  ],
  "reworkEvents": [
    { "attemptNo": 1, "ngEventId": "6c1d9a0b7e2f4c3d8a5b6c7d8e9f0a1b", "action": "RW-02: Penyetelan Ulang Kalibrasi Trip MCB", "actorName": "Rega Ariyanto", "note": "Trip 1,13 In, verifikasi ulang", "performedAtUtc": "2026-11-19T02:40:07.8331014+00:00" },
    { "attemptNo": 2, "ngEventId": "d2e3f4a5b6c7d8e9f0a1b2c3d4e5f607", "action": "RW-03: Penggantian Mekanisme Toggle & Uji Ulang", "actorName": "Rega Ariyanto", "note": "", "performedAtUtc": "2026-11-19T03:31:58.2201005+00:00" },
    { "attemptNo": 3, "ngEventId": "b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6", "action": "RW-05: Laser / Tampo Marking Ulang & Inspeksi Visual OK", "actorName": "Rega Ariyanto", "note": "", "performedAtUtc": "2026-11-19T04:15:26.7710920+00:00" }
  ],
  "okScan": {
    "serial": "2405-000842-02",
    "labelId": "NG-000842",
    "mode": "REWORK_PAIR",
    "workOrderTargetNumber": "WO-2611-B",
    "client": "PT Contoh Pelanggan B",
    "actorName": "Rega Ariyanto",
    "occurredAtUtc": "2026-11-19T04:22:10.8894412+00:00"
  }
}
```

Yang dibuktikan respons ini (semua bisa diverifikasi ulang tanpa menjalankan UI):

1. **Label tetap milik unit yang sama**: `unit.labelPayload = NG:000842` dan
   `okScan.labelId = NG-000842` — label tidak pernah dipakai ulang.
2. **Urutan kejadian stabil** walau beberapa kejadian terjadi pada detik yang sama:
   `ngEvents.seq` 1→2→3 berasal dari `UNIQUE (unit_id, seq)`.
3. **Defect sama tetap baris terpisah**: kejadian `seq=1` dan `seq=3` sama-sama
   `Solder dingin`, tidak digabung.
4. **Percobaan terhitung benar**: tiga baris rework (`attempt_no` 1,2,3) sepadan dengan
   `unit.attempts = 3`, dan setiap kejadian NG merekam percobaan yang sudah dipakai
   (`attemptNo` 0,1,2).
5. **WO asal vs WO tujuan terpisah**: `originWorkOrderNumber = WO-2611-A`,
   `targetWorkOrderNumber = WO-2611-B`, dan `okScan.workOrderTargetNumber` sama dengan target.
6. **Serial final tidak berpindah**: `unit.serial = okScan.serial`; percobaan OK kedua ditolak
   `UNIT_FINISHED`, dan serial yang sama pada unit lain ditolak `SERIAL_BOUND_OTHER`.

### 7.3 Cara merekam bukti di server uji

```powershell
$api   = "http://127.0.0.1:5080"
$token = "<token dari POST /api/v1/auth/login>"
$h     = @{ "x-trace-session" = $token; "content-type" = "application/json"; "x-trace-context-version" = "<versi dari /auth/me>" }

# 1) NG pertama
$ng = Invoke-RestMethod "$api/api/v1/ng/records/ng" -Method Post -Headers $h -Body (@{
  label = "NG:000842"; stationCode = "ST-01"; defectId = "df-01-1"; note = "Solder dingin pin 2"
  idempotencyKey = "NG-bukti-0001" } | ConvertTo-Json)

# 2) ulangi permintaan yang sama → harus reused = true dan jumlah baris tidak bertambah
$retry = Invoke-RestMethod "$api/api/v1/ng/records/ng" -Method Post -Headers $h -Body (@{
  label = "NG:000842"; stationCode = "ST-01"; defectId = "df-01-1"; note = "Solder dingin pin 2"
  idempotencyKey = "NG-bukti-0001" } | ConvertTo-Json)

# 3) rework selesai
Invoke-RestMethod "$api/api/v1/ng/records/rework" -Method Post -Headers $h -Body (@{
  unitRef = "NG:000842"; action = "RW-02: Penyetelan Ulang Kalibrasi Trip MCB"
  note = ""; idempotencyKey = "RWK-bukti-0001" } | ConvertTo-Json) | Out-Null

# 4) OK akhir pasangan label + serial
Invoke-RestMethod "$api/api/v1/ng/records/ok" -Method Post -Headers $h -Body (@{
  mode = "REWORK"; serial = "2405-000842-02"; label = "NG:000842"; note = ""
  idempotencyKey = "OK-bukti-0001" } | ConvertTo-Json) | Out-Null

# 5) rekam jejak lengkap sebagai bukti
Invoke-RestMethod "$api/api/v1/ng/units/NG:000842" -Headers $h | ConvertTo-Json -Depth 8 |
  Tee-Object -FilePath "bukti-histori-NG000842.json"
```

Bukti tambahan yang harus ikut dilampirkan: keluaran poin (5) **sebelum** dan **sesudah**
OK akhir, `$retry.reused` = `true`, serta tangkapan layar tab **Riwayat unit** yang menampilkan
timeline yang sama.

### 7.4 Bukti level basis data (opsional, auditor)

```sql
SELECT u.id, u.serial, u.label_id, u.status, u.attempts,
       u.origin_wo_number, u.target_wo_number, u.ok_at_utc
FROM units u WHERE u.label_id = 'NG-000842';

SELECT seq, attempt_no, stage_code, defect_group, defect_name, disposition, occurred_at_utc
FROM unit_ng_events WHERE unit_id = '<id unit>' ORDER BY seq;

SELECT attempt_no, ng_event_id, action, performed_at_utc
FROM unit_rework_events WHERE unit_id = '<id unit>' ORDER BY attempt_no;

SELECT serial, label_id, mode, wo_target_number, occurred_at_utc
FROM unit_ok_scans WHERE unit_id = '<id unit>';

SELECT key, operation, unit_id, result_ref FROM scan_request_keys
WHERE unit_id = '<id unit>' ORDER BY created_at_utc;
```

## 8. Layar Karantina & Rework (`trace-web`)

Rute: `/modules/traceability/quarantine` (menu **Karantina & Rework**, ikon peringatan,
hak UI `traceability.write`). Komponen: `src/components/traceability/NgReworkWorkspace.tsx`.

| Tab | Isi | Aturan tampilan |
|---|---|---|
| **Scan NG** | input label internal + pilih station + defect + catatan | fokus otomatis; setelah simpan sukses field dikosongkan dan kursor kembali ke input label; label internal ditampilkan besar untuk ditempel |
| **Rework selesai** | input label/serial + tindakan + catatan | kunci idempotensi per isi; tombol **Isi form** pada antrean karantina memindahkan unit ke form ini |
| **OK akhir** | sub-tab **Serial saja** dan **Pasangan label + serial** | mode pasangan memakai dua langkah: **Pasangkan** → panel konfirmasi (label, serial, WO tujuan) → **Simpan OK akhir**; instruksi lepas stiker hanya muncul setelah API sukses (`removeLabel`) |
| **Riwayat unit** | pencarian + filter status + tabel unit | klik baris memuat `GET /api/v1/ng/units/{unitRef}` dan menampilkan timeline NG → rework → OK |

Detail perilaku:

- **Hak per tab** berasal dari `summary.permissions` (bukan dari cookie): tab OK butuh
  `scan.unit`, tab NG/Rework butuh `ng.record`, tab Riwayat butuh `history.read`.
- **Konteks basi**: semua POST mengirim `x-trace-context-version`; balasan `CONTEXT_STALE` /
  `NO_PRODUCTION_SESSION` menampilkan tombol **Muat ulang**, tidak mengulang transaksi.
- **Retry tanpa transaksi ganda**: kunci idempotensi disimpan per isi formulir dan baru
  diganti setelah respons sukses; kegagalan jaringan atau timeout memakai kunci yang sama.
- **Pesan backend dipakai apa adanya** (`message`, `error`, `code`), termasuk rujukan label
  atau serial yang berkonflik (`LABEL_ALREADY_BOUND`, `SERIAL_BOUND_OTHER`, `WO_SAME`).
- **Tanpa WO aktif** layar menampilkan peringatan dan backend tetap menolak dengan `NO_ACTIVE_WO`.

## 9. Kode kesalahan lengkap

| Kelompok | Kode |
|---|---|
| Validasi masukan (400) | `IDEMPOTENCY_KEY`, `LABEL_REQUIRED`, `QR_NOT_INTERNAL`, `QR_TYPE`, `SERIAL_REQUIRED`, `SERIAL_INVALID`, `SERIAL_PATTERN`, `NOTE_LENGTH`, `ACTION_LENGTH`, `UNIT_REQUIRED`, `STATION_REQUIRED`, `STATION_UNKNOWN`, `DEFECT_REQUIRED`, `DEFECT_NOT_IN_STATION`, `SCAN_MODE`, `PAIR_CONFLICT`, `UNIT_STATUS` |
| Konflik status / stok (409) | `LABEL_UNKNOWN`, `LABEL_VOID`, `LABEL_ALREADY_BOUND`, `LABEL_UNBOUND`, `UNIT_UNKNOWN`, `UNIT_FINISHED`, `QC_HOLD`, `NG_DUPLICATE`, `NG_EVENT_MISSING`, `REWORK_ALREADY`, `OK_NOT_READY`, `STATE_CHANGED`, `SERIAL_FINISHED`, `SERIAL_NG_UNIT`, `SERIAL_BOUND_OTHER`, `WO_SAME`, `NO_ACTIVE_WO`, `CONTEXT_STALE`, `NO_PRODUCTION_SESSION`, `IDEMPOTENCY_CONFLICT`, `DB_BUSY` |
| Tidak ditemukan (404) | unit hasil pencatatan / hasil OK yang hilang (`FoundationErrors.NotFound`) |
| Hak akses (401/403) | sesi tidak valid, atau hak `ng.record` / `scan.unit` / `history.read` tidak dimiliki |

## 10. Konfigurasi

| Kunci | Default | Arti |
|---|---|---|
| `Trace:Ng:MaxAttempts` | `3` | batas percobaan rework per unit seumur hidup; nilai di luar 1–10 jatuh ke 3 |
| `TRACE_NG_MAX_ATTEMPTS` | `3` | nama variabel compose untuk kunci di atas (`deployment/.env.example`) |
| `x-trace-context-version` | — | header wajib konsisten dengan konteks aktif; beda → `CONTEXT_STALE` |
| `SnPattern` (konfigurasi aplikasi) | kosong | pola regex serial; kosong berarti hanya cek panjang 4–64 dan bukan QR label |

Migrasi `0004_ng_rework.sql` ditanam di assembly dan dijalankan otomatis saat start; instance
yang sudah punya `schema_versions` 1–3 hanya menjalankan migrasi ini sekali.

## 11. Verifikasi & checklist penerimaan

Uji HTTP pada Docker terisolasi mencakup serial sintetis, QR box dan NG pada kolom
serial, pola kosong/tidak valid, karantina ganda, retry, tiga rework yang berakhir OK
di WO baru, dan Tahan QC setelah NG pada uji ulang ketiga. Pencarian serial hasil
rework mengembalikan tiga kejadian NG dan tiga rework beserta WO asal dan tujuan.

Set `TRACE_NG_SERIAL_PATTERN` pada environment Docker Compose untuk memuat pola
serial saat API mulai. API mencocokkan seluruh payload dan menolak scan OK dengan
`SERIAL_FORMAT_NOT_CONFIGURED` jika pola kosong atau tidak valid. Pola sintetis
`TEST-SN-[0-9]{3}` hanya digunakan dalam uji. Pola produksi harus diverifikasi
terhadap hasil scan nyata dari serial, inner, dan outer.

Verifikasi yang sudah dijalankan:

| Perintah | Hasil |
|---|---|
| `dotnet build Traceability.sln` | Build succeeded — 0 Warning(s), 0 Error(s) |
| `npm run typecheck` (trace-web) | lolos tanpa keluaran |
| `npm run lint` (trace-web) | `Lint bersih: 89 berkas TypeScript diperiksa.` |
| `npm run build` (trace-web) | `✓ Compiled successfully`, route `/api/v1/ng/**` terdaftar |

Checklist butir penerimaan 03-NG-Rework-OK.md:

| Butir | Bukti |
|---|---|
| NG pertama mengikat label internal dan unit masuk Karantina | §3.1 T1, §5.3 |
| Rework menambah percobaan dan mengembalikan unit ke Uji ulang | §3.1 T4, §5.5 |
| NG ulang membuat baris baru, defect sama tetap terpisah | §3.1 T2/T3, §7.2 butir 3 |
| Percobaan habis → Tahan QC; percobaan ke-4 ditolak | §4, §3.2 |
| OK biasa menerima serial valid saja | §5.6, `ValidateSerial` |
| OK rework mengikat label + serial + WO tujuan satu transaksi | §3.1 T6, §5.7 |
| Retry tidak menggandakan transaksi; konflik dilaporkan dengan rujukan | §6, §5.3–§5.7 pesan error |

## 12. Item terbuka

1. **Penerimaan scanner produksi** menunggu contoh payload nyata dan penetapan aturan
   NG saat serial sudah terbaca. Uji HTTP memakai payload sintetis.
2. **Disposisi QC** (rilis/scrap dari `QC_HOLD`) belum punya operasi API — dijadwalkan pada
   Fase 4/5 bersama packing.
3. **Packing** baru membaca `UnitStatus.BlocksPacking` (Karantina, Uji ulang, Tahan QC) sebagai
   kontrak; filter nyata dipasang saat Fase 4 mengerjakan gelombang packing.
4. `stage_code` pada `units` menyimpan pos kejadian NG terakhir sebagai petunjuk tempat unit
   berada — bukan riwayat lengkap; riwayat lengkap ada di `unit_ng_events`.
