CREATE TABLE packing_boxes (
    qr TEXT PRIMARY KEY,
    kind TEXT NOT NULL CHECK (kind IN ('INNER', 'OUTER')),
    wo TEXT NOT NULL,
    sku TEXT NOT NULL,
    client TEXT NOT NULL,
    shipping TEXT NOT NULL,
    opened_by TEXT NOT NULL,
    opened_at TEXT NOT NULL,
    closed_by TEXT,
    closed_at TEXT
);

CREATE TABLE packing_units (
    unit_id TEXT PRIMARY KEY REFERENCES units(id),
    inner_qr TEXT NOT NULL REFERENCES packing_boxes(qr),
    actor TEXT NOT NULL,
    scanned_at TEXT NOT NULL
);
CREATE INDEX ix_packing_units_inner ON packing_units(inner_qr);

CREATE TABLE packing_inners (
    inner_qr TEXT PRIMARY KEY REFERENCES packing_boxes(qr),
    outer_qr TEXT NOT NULL REFERENCES packing_boxes(qr),
    actor TEXT NOT NULL,
    scanned_at TEXT NOT NULL
);
CREATE INDEX ix_packing_inners_outer ON packing_inners(outer_qr);
