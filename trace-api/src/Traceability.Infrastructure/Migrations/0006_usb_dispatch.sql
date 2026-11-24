CREATE TABLE usb_print_dispatch (
    job_id TEXT PRIMARY KEY REFERENCES print_jobs(id),
    actor_id TEXT NOT NULL REFERENCES accounts(id),
    state TEXT NOT NULL CHECK (state IN ('CLAIMED','SPOOLED','FAILED','UNKNOWN')),
    claimed_at TEXT NOT NULL,
    reported_at TEXT
);
