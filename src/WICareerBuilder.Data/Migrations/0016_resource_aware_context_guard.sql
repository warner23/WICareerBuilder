-- v0.8.4 resource-aware context guard.
-- Refines v0.8.3 so generic development/project folders no longer suppress legitimate
-- project evidence, technical documentation and books merely because an ancestor is named
-- software/code/engineering_programs. Hard application/runtime branches remain pruned.

ALTER TABLE discovery_scan_runs ADD COLUMN policy_restored_resources INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN context_role_suggestions INTEGER NOT NULL DEFAULT 0;

UPDATE settings SET setting_value = '16' WHERE setting_key = 'data_format_version';
