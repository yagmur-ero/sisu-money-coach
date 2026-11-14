# Current project decisions and implementation notes

- Project name and namespaces: `PersonalEconomicCoach.Api`.
- Exactly one database: existing MySQL database `spankki`.
- No `moneymind_app` database and no `app_users` table.
- Existing banking tables `transactions` and `transactions_raw` are not modified.
- API reads `spankki.transactions` directly; `transactions_raw` remains untouched.
- No `api_transactions` or `api_accounts` views are required or created.
- Account balances are derived from the latest transaction per customer/account.
- Application-owned tables: `planned_events`, `user_plans`, `basket_overrides`, `conversation_threads`, `conversation_messages`.
- Existing `customer_id` is used to scope all financial and application data.
- Conversation history is persisted by `/ask`; additive read endpoints are `/conversations` and `/conversations/{id}/messages`.
- Existing frontend endpoint URLs are preserved.
- Financial calculations remain deterministic; AI generates explanations only.

## Database safeguards

`sql/schema.sql` contains only `CREATE TABLE IF NOT EXISTS` statements for application-owned tables. It does not alter any banking table. The optional API user script grants SELECT on `transactions`, and write permissions only to application-owned tables.

## Data assumptions to verify

The screenshots show negative `tx_amount_eur` for expense transactions. The current direction derivation treats transfer-like types as transfers, refund/cashback types as refunds, other negative amounts as expenses, and other positive amounts as income. Verify the full `transaction_type` distribution before relying on production-grade figures.
