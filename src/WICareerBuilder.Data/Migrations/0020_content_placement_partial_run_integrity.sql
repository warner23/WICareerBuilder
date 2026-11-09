PRAGMA foreign_keys = ON;

ALTER TABLE content_placement_runs ADD COLUMN partially_organised INTEGER NOT NULL DEFAULT 0;

UPDATE settings SET setting_value = '20' WHERE setting_key = 'data_format_version';
