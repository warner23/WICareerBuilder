ALTER TABLE resource_analysis ADD COLUMN suggested_topic TEXT NOT NULL DEFAULT 'Unclassified';
ALTER TABLE resource_analysis ADD COLUMN suggested_subtopics_json TEXT NOT NULL DEFAULT '[]';
UPDATE settings SET setting_value = '3' WHERE setting_key = 'data_format_version';
