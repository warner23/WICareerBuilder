PRAGMA foreign_keys = ON;

ALTER TABLE resource_classification_proposals ADD COLUMN subject_confidence REAL NOT NULL DEFAULT 0.0;
ALTER TABLE resource_classification_proposals ADD COLUMN topic_confidence REAL NOT NULL DEFAULT 0.0;
ALTER TABLE resource_classification_proposals ADD COLUMN subtopic_confidence REAL NOT NULL DEFAULT 0.0;

ALTER TABLE content_placement_items ADD COLUMN subject_confidence REAL NOT NULL DEFAULT 0.0;
ALTER TABLE content_placement_items ADD COLUMN topic_confidence REAL NOT NULL DEFAULT 0.0;
ALTER TABLE content_placement_items ADD COLUMN subtopic_confidence REAL NOT NULL DEFAULT 0.0;

UPDATE settings SET setting_value = '19' WHERE setting_key = 'data_format_version';
