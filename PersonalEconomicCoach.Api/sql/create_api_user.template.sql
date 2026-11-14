-- Optional restricted database account for the API.
-- Run as a MySQL administrator after sql/schema.sql has created app tables.
-- This grants READ ONLY access to S-Pankki's existing transactions table.
-- It grants write access only to PersonalEconomicCoach-owned tables.

CREATE USER IF NOT EXISTS 'personal_economic_coach_api'@'%' IDENTIFIED BY 'REPLACE_WITH_A_STRONG_PASSWORD';

GRANT SELECT ON spankki.transactions
TO 'personal_economic_coach_api'@'%';

GRANT SELECT, INSERT, UPDATE, DELETE ON spankki.planned_events
TO 'personal_economic_coach_api'@'%';

GRANT SELECT, INSERT, UPDATE, DELETE ON spankki.user_plans
TO 'personal_economic_coach_api'@'%';

GRANT SELECT, INSERT, UPDATE, DELETE ON spankki.basket_overrides
TO 'personal_economic_coach_api'@'%';

GRANT SELECT, INSERT, UPDATE, DELETE ON spankki.conversation_threads
TO 'personal_economic_coach_api'@'%';

GRANT SELECT, INSERT, UPDATE, DELETE ON spankki.conversation_messages
TO 'personal_economic_coach_api'@'%';

FLUSH PRIVILEGES;

-- Intentionally NOT granted:
-- INSERT / UPDATE / DELETE / ALTER / DROP on transactions or transactions_raw.
