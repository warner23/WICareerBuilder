CREATE TABLE IF NOT EXISTS learning_block_comparisons (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    candidate_id INTEGER NOT NULL,
    module_id INTEGER NOT NULL,
    existing_block_id INTEGER NOT NULL,
    similarity REAL NOT NULL,
    status TEXT NOT NULL DEFAULT 'Pending',
    resolution TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    resolved_utc TEXT NULL,
    UNIQUE(candidate_id, module_id, existing_block_id),
    FOREIGN KEY(candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE,
    FOREIGN KEY(module_id) REFERENCES learning_modules(id) ON DELETE CASCADE,
    FOREIGN KEY(existing_block_id) REFERENCES learning_blocks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_learning_block_comparisons_status
ON learning_block_comparisons(status, module_id);
UPDATE settings SET setting_value = '10' WHERE setting_key = 'data_format_version';
