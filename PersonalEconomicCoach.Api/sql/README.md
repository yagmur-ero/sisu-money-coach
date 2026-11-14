# Database setup — one existing database only

The API uses the existing MySQL database `spankki`.

## Banking data (owned by S-Pankki / the dataset)

- `transactions` — the processed transaction table read by the API.
- `transactions_raw` — the original raw import. The API does not query or modify this table.

**Do not rename, alter, update, delete, truncate, or add columns/indexes to either banking table.** The API queries `transactions` directly. It does not require banking views or copied transaction tables.

## Application data (owned by PersonalEconomicCoach)

`schema.sql` creates only these new tables inside the same `spankki` database:

- `planned_events`
- `user_plans`
- `basket_overrides`
- `conversation_threads`
- `conversation_messages`

These tables use the existing S-Pankki `customer_id` as the owner key. There is no new `app_users` table and no second database.

## Setup order

1. Confirm that the existing `spankki.transactions` table exists and contains the processed transaction data.
2. Run `schema.sql` in MySQL Workbench. It creates only the five application-owned tables listed above.
3. Run `verify_database.sql` to inspect the source rows and confirm that the app tables exist.
4. Configure `ConnectionStrings:Default` to connect to the existing `spankki` database.
5. Start the API and test `/health`, `/health/ready`, `/accounts`, and `/transactions`.

## Optional API database user

`create_api_user.template.sql` creates a restricted account. It receives:

- `SELECT` only on `spankki.transactions`.
- `SELECT`, `INSERT`, `UPDATE`, and `DELETE` on the five application-owned tables.
- No permissions to modify `transactions` or `transactions_raw`.

Replace the placeholder password before running it.

## Amount and direction assumptions

The screenshots shared during development show negative values for expenses in `transactions.tx_amount_eur`. The API keeps this signed amount unchanged in `transactions` and derives a direction for calculations:

- `transaction_type` containing `transfer` => transfer (excluded from income/expense totals).
- `transaction_type` containing `refund` or `cashback` => refund (excluded from income totals and expense baskets).
- Otherwise, negative amount => expense.
- Otherwise, positive amount => income.

Verify this mapping against the full `transaction_type` distribution before relying on financial insights. The API never writes normalized amounts back to the banking table.
