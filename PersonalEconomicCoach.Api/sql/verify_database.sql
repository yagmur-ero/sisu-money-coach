-- Read-only verification queries. Run in MySQL Workbench after sql/schema.sql.
-- No banking data is modified by this script.

USE spankki;

-- 1. Confirm the existing processed banking table is readable.
SELECT COUNT(*) AS transaction_count
FROM transactions;

-- 2. Inspect a few existing transaction rows.
SELECT
    id,
    customer_id,
    bank_account_id,
    tx_date,
    tx_amount_eur,
    balance_after_eur,
    transaction_type,
    merchant_id,
    mcc_code
FROM transactions
ORDER BY tx_date DESC, tx_timestamp DESC, id DESC
LIMIT 10;

-- 3. Confirm the application-owned tables exist.
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'spankki'
  AND table_type = 'BASE TABLE'
  AND table_name IN (
      'planned_events',
      'user_plans',
      'basket_overrides',
      'conversation_threads',
      'conversation_messages'
  )
ORDER BY table_name;

-- 4. Check transaction amount signs and source transaction types.
SELECT
    transaction_type,
    COUNT(*) AS row_count,
    SUM(tx_amount_eur < 0) AS negative_amount_count,
    SUM(tx_amount_eur > 0) AS positive_amount_count
FROM transactions
GROUP BY transaction_type
ORDER BY row_count DESC;
