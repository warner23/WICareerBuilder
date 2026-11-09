CREATE TABLE IF NOT EXISTS resource_analysis (
    resource_id INTEGER PRIMARY KEY,
    analysis_status TEXT NOT NULL,
    scanner_kind TEXT NOT NULL,
    scanner_version TEXT NOT NULL,
    scanned_utc TEXT NOT NULL,
    suggested_resource_type TEXT NOT NULL DEFAULT 'Unknown',
    document_title TEXT NULL,
    author TEXT NULL,
    page_count INTEGER NULL,
    pages_sampled INTEGER NOT NULL DEFAULT 0,
    extracted_characters INTEGER NOT NULL DEFAULT 0,
    has_searchable_text INTEGER NOT NULL DEFAULT 0,
    headings_json TEXT NOT NULL DEFAULT '[]',
    keywords_json TEXT NOT NULL DEFAULT '[]',
    content_indicators_json TEXT NOT NULL DEFAULT '[]',
    text_preview TEXT NOT NULL DEFAULT '',
    error_message TEXT NULL,
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_resource_analysis_status ON resource_analysis(analysis_status);
UPDATE settings SET setting_value = '2' WHERE setting_key = 'data_format_version';
