-- v0.8.1 source integrity & discovery audit.
-- Deep/fingerprint stages are allowed only after discovery has accepted a resource.

UPDATE resources
SET fingerprint_status = 'Deferred',
    analysis_needed = 0
WHERE is_policy_excluded = 1
   OR is_ignored = 1
   OR discovery_decision <> 'Include';

UPDATE resources
SET fingerprint_status = CASE
        WHEN is_available = 0 THEN 'Unavailable'
        WHEN quick_fingerprint <> '' THEN 'Complete'
        ELSE 'Pending' END,
    analysis_needed = CASE
        WHEN is_available = 0 THEN 0
        WHEN analysis_status IN ('Analysed','Limited') THEN 0
        ELSE 1 END
WHERE is_policy_excluded = 0
  AND is_ignored = 0
  AND discovery_decision = 'Include';

CREATE INDEX IF NOT EXISTS idx_resources_deep_scan_eligibility
    ON resources(library_source_id, discovery_decision, is_policy_excluded, is_ignored, is_available, analysis_needed);

UPDATE settings SET setting_value = '13' WHERE setting_key = 'data_format_version';
