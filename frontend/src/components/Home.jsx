import { formatDate } from "../dates";

function money(n, signed = false) {
  const s = Math.abs(n).toLocaleString("fi-FI", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const sign = n < 0 ? "-" : signed && n > 0 ? "+" : "";
  return `${sign}${s} €`;
}

const PILL_TEXT = { green: "On track", amber: "Be careful", red: "Stop spending" };

export default function Home({ home, onAsk }) {
  if (home === undefined) return <div className="home-card muted">Loading your overview...</div>;
  if (home === null) return <div className="home-card muted">Could not load your overview.</div>;

  const { balance, accounts, recent, month, budget } = home;
  const net = month.income - month.expenses;
  const barMax = Math.max(month.income, month.expenses, 1);

  const spentPct = Math.min(100, Math.round((month.expenses / budget.monthly) * 100));
  const budgetState =
    month.expenses >= budget.monthly ? "red" : month.expenses >= budget.monthly * 0.8 ? "amber" : "green";

  const ratio = balance > 0 ? budget.safe_to_spend / balance : 0;
  const safeState =
    budget.safe_to_spend <= 0 ? "red" : ratio >= 0.25 ? "green" : ratio >= 0.1 ? "amber" : "red";
  const perDay = budget.days_left > 0 ? Math.round(budget.safe_to_spend / budget.days_left) : 0;

  return (
    <div>
      {/* Account balance */}
      <div className="home-card">
        <div className="home-label">Account balance</div>
        <div className={"balance-big" + (balance < 0 ? " neg" : "")}>{money(balance)}</div>
        {balance < 0 && (
          <div className="note neg-note">
            Your balance is below zero.{" "}
            <button className="see-more" onClick={() => onAsk(4, "How much can I safely spend?")}>
              See how to get back up
            </button>
          </div>
        )}
        {accounts.map((a) => (
          <div className="acct-row" key={a.id}>
            <span>{a.name}</span>
            <b>{money(a.balance)}</b>
          </div>
        ))}
      </div>

      {/* Income and expenses */}
      <div className="home-card">
        <div className="home-label">Income and expenses · this month</div>
        <div className="ie-row"><span>Income</span><b>{money(month.income)}</b></div>
        <div className="bar thick"><div className="bar-fill green" style={{ width: `${(month.income / barMax) * 100}%` }} /></div>
        <div className="ie-row"><span>Expenses</span><b>{money(month.expenses)}</b></div>
        <div className="bar thick"><div className="bar-fill amber" style={{ width: `${(month.expenses / barMax) * 100}%` }} /></div>
        <div className="ie-row">
          <span>Left over</span>
          <b className={net < 0 ? "up" : "down"}>{money(net, true)}</b>
        </div>
      </div>

      {/* Budget and safe-to-spend */}
      <div className="home-card">
        <div className="home-label">Budget and safe-to-spend</div>
        <div className="ie-row">
          <span>{money(month.expenses)} of {money(budget.monthly)} budget</span>
          <b>{spentPct}%</b>
        </div>
        <div className="bar thick"><div className={"bar-fill " + budgetState} style={{ width: `${spentPct}%` }} /></div>

        <div className="safe-row">
          <div>
            <div className="balance-big">{money(budget.safe_to_spend)}</div>
            <div className="muted">safe to spend, about {perDay} € a day for {budget.days_left} days</div>
          </div>
          <span className={"pill " + safeState}>{PILL_TEXT[safeState]}</span>
        </div>
        <button className="see-more" onClick={() => onAsk(4, "How much can I safely spend?")}>
          See the breakdown
        </button>
      </div>

      {/* Recent transactions */}
      <div className="home-card">
        <div className="home-label">Recent transactions</div>
        <div className="tx-list">
          {recent.map((t) => (
            <div className="tx-row" key={t.id}>
              <div className="leak-rank">{t.title[0].toUpperCase()}</div>
              <div className="tx-main">
                <div><b>{t.title}</b></div>
                <div className="muted">{t.category} · {formatDate(t.date)}</div>
              </div>
              <div className={"tx-amt" + (t.amount > 0 ? " pos" : "")}>{money(t.amount, true)}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}