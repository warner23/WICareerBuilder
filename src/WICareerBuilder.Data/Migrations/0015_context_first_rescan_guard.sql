-- v0.8.3 context-first rescan guard.
-- Directory context now has precedence over supported file extensions. Known application/web
-- archive branches can be pruned before file enumeration, and scanner-created leakage from the
-- immediately preceding scan can be safely reconciled without touching original drive files.

ALTER TABLE discovery_scan_runs ADD COLUMN context_directories_skipped INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN pruned_branch_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN pruned_catalogue_rows INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN reconciled_leakage_removed INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN protected_leakage_rows INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS discovery_scan_pruned_branches (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    discovery_scan_run_id INTEGER NOT NULL,
    library_source_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    reason TEXT NOT NULL,
    existing_catalogue_rows INTEGER NOT NULL DEFAULT 0,
    safely_removed_rows INTEGER NOT NULL DEFAULT 0,
    protected_rows INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    FOREIGN KEY(discovery_scan_run_id) REFERENCES discovery_scan_runs(id) ON DELETE CASCADE,
    FOREIGN KEY(library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE,
    UNIQUE(discovery_scan_run_id, relative_path)
);

CREATE INDEX IF NOT EXISTS idx_discovery_scan_pruned_branches_run
    ON discovery_scan_pruned_branches(discovery_scan_run_id, relative_path);

CREATE TABLE IF NOT EXISTS discovery_reconciliation_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    discovery_scan_run_id INTEGER NOT NULL,
    library_source_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    action TEXT NOT NULL,
    reason TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY(discovery_scan_run_id) REFERENCES discovery_scan_runs(id) ON DELETE CASCADE,
    FOREIGN KEY(library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_discovery_reconciliation_log_run
    ON discovery_reconciliation_log(discovery_scan_run_id, action, relative_path);

UPDATE settings SET setting_value = '15' WHERE setting_key = 'data_format_version';
