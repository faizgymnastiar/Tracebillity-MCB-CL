-- Migrasi 0002: seed master produksi dari SSOT §2 dan §3.
-- Hanya data referensi riil (keluarga, SKU, shift, line, tahap, defect).
-- TIDAK ada akun demo atau password bawaan (SSOT §9).
-- Idempoten: dijalankan ulang tidak menghasilkan duplikasi.

INSERT OR IGNORE INTO product_families (id, code, name, status)
VALUES ('fam-mcb-cl', 'MCB CL', 'MCB CL (Miniature Circuit Breaker - Current Limiting)', 'ACTIVE');

INSERT OR IGNORE INTO product_skus (id, family_id, code, name, status) VALUES
 ('sku-p1-02', 'fam-mcb-cl', 'MCB CL P1 - 2 A',  'MCB CL P1 - 2 A',  'ACTIVE'),
 ('sku-p1-04', 'fam-mcb-cl', 'MCB CL P1 - 4 A',  'MCB CL P1 - 4 A',  'ACTIVE'),
 ('sku-p1-06', 'fam-mcb-cl', 'MCB CL P1 - 6 A',  'MCB CL P1 - 6 A',  'ACTIVE'),
 ('sku-p1-10', 'fam-mcb-cl', 'MCB CL P1 - 10 A', 'MCB CL P1 - 10 A', 'ACTIVE'),
 ('sku-p1-16', 'fam-mcb-cl', 'MCB CL P1 - 16 A', 'MCB CL P1 - 16 A', 'ACTIVE'),
 ('sku-p1-20', 'fam-mcb-cl', 'MCB CL P1 - 20 A', 'MCB CL P1 - 20 A', 'ACTIVE'),
 ('sku-p1-25', 'fam-mcb-cl', 'MCB CL P1 - 25 A', 'MCB CL P1 - 25 A', 'ACTIVE'),
 ('sku-p1-35', 'fam-mcb-cl', 'MCB CL P1 - 35 A', 'MCB CL P1 - 35 A', 'ACTIVE'),
 ('sku-p1-50', 'fam-mcb-cl', 'MCB CL P1 - 50 A', 'MCB CL P1 - 50 A', 'ACTIVE'),
 ('sku-p3-10', 'fam-mcb-cl', 'MCB CL P3 - 10 A', 'MCB CL P3 - 10 A', 'ACTIVE'),
 ('sku-p3-16', 'fam-mcb-cl', 'MCB CL P3 - 16 A', 'MCB CL P3 - 16 A', 'ACTIVE'),
 ('sku-p3-20', 'fam-mcb-cl', 'MCB CL P3 - 20 A', 'MCB CL P3 - 20 A', 'ACTIVE'),
 ('sku-p3-25', 'fam-mcb-cl', 'MCB CL P3 - 25 A', 'MCB CL P3 - 25 A', 'ACTIVE'),
 ('sku-p3-35', 'fam-mcb-cl', 'MCB CL P3 - 35 A', 'MCB CL P3 - 35 A', 'ACTIVE');

INSERT OR IGNORE INTO shifts (id, code, name, status) VALUES
 ('shift-1', 'S1', 'Shift 1', 'ACTIVE'),
 ('shift-2', 'S2', 'Shift 2', 'ACTIVE'),
 ('shift-3', 'S3', 'Shift 3', 'ACTIVE');

INSERT OR IGNORE INTO lines (id, code, name, status) VALUES
 ('line-01', 'L01', 'LINE 01 - MCB CL', 'ACTIVE'),
 ('line-02', 'L02', 'LINE 02 - MCB CL', 'ACTIVE');

-- Tahap per tabel flowchart SSOT §2 (serial dilaser di ST-08, scan akhir ST-09).
INSERT OR IGNORE INTO stages (id, family_id, code, name, seq, status) VALUES
 ('st-01', 'fam-mcb-cl', 'ST-01', 'Rivet', 1, 'ACTIVE'),
 ('st-02', 'fam-mcb-cl', 'ST-02', 'Thermal Adjuster', 2, 'ACTIVE'),
 ('st-03', 'fam-mcb-cl', 'ST-03', 'Magnetic Test 4 IN', 3, 'ACTIVE'),
 ('st-04', 'fam-mcb-cl', 'ST-04', 'Magnetic Test 6 IN', 4, 'ACTIVE'),
 ('st-05', 'fam-mcb-cl', 'ST-05', 'High Voltage', 5, 'ACTIVE'),
 ('st-06', 'fam-mcb-cl', 'ST-06', 'Pad Printing', 6, 'ACTIVE'),
 ('st-07', 'fam-mcb-cl', 'ST-07', 'Accessories', 7, 'ACTIVE'),
 ('st-08', 'fam-mcb-cl', 'ST-08', 'Laser QR', 8, 'ACTIVE'),
 ('st-09', 'fam-mcb-cl', 'ST-09', 'Scan & Packing', 9, 'ACTIVE');

-- Setiap baris = satu pilihan defect dengan ID tersendiri, walau kode grup DF berulang.
INSERT OR IGNORE INTO defects (id, stage_id, group_code, name, status) VALUES
 ('df-01-1', 'st-01', 'DF-01', 'Missing Rivet', 'ACTIVE'),
 ('df-01-2', 'st-01', 'DF-01', 'Rivet Crack', 'ACTIVE'),
 ('df-02-1', 'st-02', 'DF-02', 'Early Trip', 'ACTIVE'),
 ('df-02-2', 'st-02', 'DF-02', 'Late Trip', 'ACTIVE'),
 ('df-02-3', 'st-02', 'DF-02', 'Handle Loss', 'ACTIVE'),
 ('df-03-1', 'st-03', 'DF-03', 'Trip di atas 0,2 detik', 'ACTIVE'),
 ('df-04-1', 'st-04', 'DF-04', 'Trip di bawah 0,2 detik', 'ACTIVE'),
 ('df-05-1', 'st-05', 'DF-05', 'Kebocoran arus saat disuplai 2 kV', 'ACTIVE'),
 ('df-06-1', 'st-06', 'DF-06', 'Printing kurang tebal', 'ACTIVE'),
 ('df-06-2', 'st-06', 'DF-06', 'Printing miring', 'ACTIVE'),
 ('df-07-1', 'st-07', 'DF-07', 'ACC tidak lengkap', 'ACTIVE'),
 ('df-08-1', 'st-08', 'DF-08', 'Marking laser miring', 'ACTIVE'),
 ('df-08-2', 'st-08', 'DF-08', 'Marking laser tidak inject', 'ACTIVE');
