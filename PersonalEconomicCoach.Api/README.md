# PersonalEconomicCoach.Api

ASP.NET Core Web API for the S-Pankki Personal Economic Trainer hackathon challenge.

## Technology

- .NET 10 / C#
- MySQL through MySqlConnector
- Dapper for SQL queries
- OpenAI Chat Completions API for answer phrasing

## Database rule

The project uses exactly one existing MySQL database: `spankki`.

The API reads `spankki.transactions` directly and never modifies it. The original `spankki.transactions_raw` table is left untouched and is not queried by the API. The API creates and writes only its own application tables inside `spankki`:

- `planned_events`
- `user_plans`
- `basket_overrides`
- `conversation_threads`
- `conversation_messages`

The existing S-Pankki `customer_id` is the customer identity. There is no second database and no `app_users` table.

See [DATABASE_INTEGRATION.md](DATABASE_INTEGRATION.md) for setup instructions and database safeguards.

## Configure

Set the connection string using .NET user secrets or an environment variable. PowerShell example:

```powershell
$env:ConnectionStrings__Default = "Server=YOUR_MYSQL_HOST;Port=3306;Database=spankki;User ID=YOUR_API_USER;Password=YOUR_PASSWORD;Allow User Variables=true;"
$env:App__DemoCustomerId = "CUST00001"
$env:Ai__ApiKey = "YOUR_OPENAI_API_KEY"
```

For development, `App:DemoCustomerId` selects one existing S-Pankki dataset customer. Do not accept arbitrary customer IDs from request bodies. Replace the demo context with an authenticated customer claim before production.

## Run

```powershell
dotnet restore
dotnet build
dotnet run --urls http://localhost:5080
```

## Endpoints

Existing frontend endpoints remain unchanged:

- `GET /health`
- `GET /health/ready` (additional database readiness check)
- `POST /ask`
- `POST /events`
- `GET /events`
- `DELETE /events/{id}`
- `GET /plan`
- `PUT /plan`
- `PUT /plan/overrides`
- `GET /setup/uncertain`
- `GET /insights/baskets`
- `GET /insights/comparison`
- `GET /insights/leaks`
- `GET /insights/safe-to-spend`
- `POST /insights/what-if`
- `GET /accounts`
- `GET /transactions`

Conversation history is also available through additive endpoints:

- `GET /conversations`
- `GET /conversations/{id}/messages`

`POST /ask` accepts an optional `conversation_id`. If omitted, a new conversation is created. The response includes `conversation_id` for subsequent questions.

## Financial calculation rules

- The source signed amount `transactions.tx_amount_eur` is never modified.
- Direction is derived for calculations: transfer-like types are excluded; refund/cashback types are excluded from income and spending baskets; otherwise negative amounts are expenses and positive amounts are income.
- Basket classification, historical averages, safe-to-spend and month-end projection are calculated by SQL/C# logic, not by the LLM.
- Expected monthly income and expenses use the previous three fully covered completed calendar months where available.
- Safe-to-spend-now uses the latest recorded account balance, less commitments due before the next configured payday, irregular reserve, safety buffer and prorated savings goal. It does not add expected future income.
- Month-end projection is separate: latest recorded account balance + estimated remaining monthly income - estimated remaining monthly expenses - planned events through month end. It is an estimate, not a guaranteed balance.
- The API returns the account balance date so clients can see whether the imported banking data is stale.

## AI behavior

The MVP uses deterministic routing for the five supported financial questions. The API calculates the data first; the LLM only writes the explanation in the selected tone. Direct `/insights` endpoints work without AI.

## Tests

Build with `dotnet build`. Database integration requires the existing `spankki.transactions` table and the five application-owned tables created by `sql/schema.sql`.
