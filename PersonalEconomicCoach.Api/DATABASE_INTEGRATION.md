# Database integration — existing `spankki` database

## Architecture rule

There is exactly **one database: `spankki`**.

The API reads the existing processed banking table `spankki.transactions` directly. It does not create or depend on `api_transactions` or `api_accounts` views, and it does not query or modify `transactions_raw`.

The API creates and writes only its own application tables in the same `spankki` database:

- `planned_events`
- `user_plans`
- `basket_overrides`
- `conversation_threads`
- `conversation_messages`

The existing S-Pankki `customer_id` is used throughout. There is no `app_users` table.

## 1. Verify the existing source table

Run in MySQL Workbench:

```sql
USE spankki;
SHOW CREATE TABLE transactions;
SELECT COUNT(*) AS transaction_count FROM transactions;
SELECT * FROM transactions ORDER BY tx_date DESC, tx_timestamp DESC, id DESC LIMIT 5;
```

Expected source columns, based on the structure inspected during development:

- `id`
- `customer_id`
- `bank_account_id`
- `tx_date`
- `tx_timestamp`
- `category`
- `tx_amount_eur`
- `balance_after_eur`
- `transaction_type`
- `merchant_id`
- `mcc_code`

If your actual `SHOW CREATE TABLE` output differs, stop and map the exact names before running the API. Do not alter the banking table to match the API.

## 2. Create application-owned tables

Open and execute `sql/schema.sql` in MySQL Workbench.

It starts with `USE spankki;` and contains only `CREATE TABLE IF NOT EXISTS` statements for the five application-owned tables. It does not issue `ALTER TABLE`, `UPDATE`, `DELETE`, `TRUNCATE`, or `CREATE VIEW` against the banking tables.

## 3. Verify the setup

Execute `sql/verify_database.sql`.

It reads sample banking rows, checks the existing transaction count, and lists the five application tables. It does not modify banking records.

## 4. Configure the API connection

In `appsettings.json`, `ConnectionStrings:Default` must point to the existing database:

```text
Server=YOUR_MYSQL_HOST;Port=3306;Database=spankki;User ID=YOUR_API_USER;Password=YOUR_PASSWORD;
```

For PowerShell, you can override it without writing a password into source control:

```powershell
$env:ConnectionStrings__Default = "Server=YOUR_MYSQL_HOST;Port=3306;Database=spankki;User ID=YOUR_API_USER;Password=YOUR_PASSWORD;Allow User Variables=true;"
$env:App__DemoCustomerId = "CUST00001"
```

If ASP.NET Core runs inside Docker, use the MySQL host name reachable from that container. `localhost` inside the API container means the API container itself, not your Windows host or your teammate's computer.

## 5. Start and test the API

```powershell
dotnet restore
dotnet build
dotnet run --urls http://localhost:5080
```

Test:

```text
GET http://localhost:5080/health
GET http://localhost:5080/health/ready
GET http://localhost:5080/accounts
GET http://localhost:5080/transactions
```

`/health` checks API liveness. `/health/ready` checks the database connection, the existing `transactions` table, and the five application-owned tables.

## 6. Database permissions

For a dedicated API account, adapt and run `sql/create_api_user.template.sql` as a MySQL administrator. It grants read-only access to `spankki.transactions` and CRUD only on the application-owned tables. It deliberately grants no write permission on `transactions` or `transactions_raw`.

## Important safeguards

- Do not execute any script that alters the existing `transactions` or `transactions_raw` tables.
- Do not create a second database.
- Do not create an `app_users` table; use the existing customer ID from configuration / the authenticated customer context.
- The API's account endpoint derives the latest balance per bank account from `transactions.balance_after_eur`.
- Direction is derived in SQL from the signed `tx_amount_eur` and transaction type. Verify all transaction types against the dataset before relying on the totals.
