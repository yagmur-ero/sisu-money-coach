import { useState, useEffect } from "react";
import { daysUntil } from "../dates";
import { savePlan, whatIf, daysBetween } from "../api";

const eur = (n) => `${Math.round(Number(n) || 0)} €`;
const eur1 = (n) => `${Math.round((Number(n) || 0) * 10) / 10} €`;
const signed = (n) => (n < 0 ? `-${eur(-n)}` : eur(n));

// The backend sends no safe limits per basket, so the app derives them (change here)
const LIMITS = { regularPctOfIncome: 0.5, leaksPctOfIncome: 0.1 };

/* ---------- 1. Baskets ---------- */
function toBasketsView(data, adjusted) {
  if (Array.isArray(data.baskets)) return data; // mock shape

  const income = Number(data.income) || 0;
  const est = data.irregular_estimate ?? {};
  const expected = Number(adjusted ?? est.adjusted ?? est.average_per_month ?? 0);

  const byCategory = {};
  for (const i of data.leak_items ?? []) {
    const key = i.category || i.merchant;
    byCategory[key] = (byCategory[key] || 0) + Number(i.total_amount);
  }
  const leakItems = Object.entries(byCategory)
    .map(([name, amount]) => ({ name, amount }))
    .sort((a, b) => b.amount - a.amount)
    .slice(0, 6);

  return {
    income,
    spent: Number(data.expenses) || 0,
    baskets: [
      { key: "regular", label: "Mandatory, regular", amount: Number(data.baskets.regular), limit: Math.max(income * LIMITS.regularPctOfIncome, 1), items: [] },
      { key: "irregular", label: "Mandatory, irregular", amount: Number(data.baskets.irregular), limit: Math.max(expected, 1), items: [] },
      { key: "leaks", label: "Everyday leaks", amount: Number(data.baskets.leaks), limit: Math.max(income * LIMITS.leaksPctOfIncome, 1), items: leakItems },
    ],
  };
}

function basketState(b) {
  const ratio = b.amount / b.limit;
  if (b.key === "regular") return "green"; // fixed costs stay calm
  if (b.key === "irregular") return ratio >= 1 ? "amber" : "green";
  return ratio >= 1 ? "red" : ratio >= 0.8 ? "amber" : "green"; // leaks
}

function IrregularAdjust({ average, adjusted, onSaved }) {
  const [value, setValue] = useState(String(Math.round(adjusted ?? average ?? 0)));
  const [status, setStatus] = useState("");

  async function save() {
    const n = Number(String(value).replace(",", "."));
    if (!(n >= 0)) return setStatus("Enter 0 or more.");
    try {
      await savePlan({ irregular_monthly_adjusted: n });
      onSaved(n);
      setStatus("Saved.");
    } catch (e) {
      setStatus("Could not save: " + e.message);
    }
  }

  async function reset() {
    try {
      await savePlan({ irregular_monthly_adjusted: null });
      onSaved(null);
      setValue(String(Math.round(average ?? 0)));
      setStatus("Back to the automatic average.");
    } catch (e) {
      setStatus("Could not save: " + e.message);
    }
  }

  return (
    <div className="adjust">
      <div className="inside-title">Expected irregular costs per month</div>
      <div className="muted">
        Average of past months: {eur(average)}
        {adjusted != null ? `, your setting: ${eur(adjusted)}` : ""}
      </div>
      <div className="adjust-row">
        <input className="text-field" inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} />
        <button className="adjust-btn" onClick={save}>Save</button>
        {adjusted != null && <button className="adjust-btn ghost" onClick={reset}>Reset</button>}
      </div>
      {status && <div className="muted">{status}</div>}
    </div>
  );
}

export function Baskets({ data }) {
  const [ready, setReady] = useState(false);
  const [open, setOpen] = useState("leaks");
  const [adjusted, setAdjusted] = useState(data.irregular_estimate?.adjusted ?? null);

  // Start empty, then fill, so the tanks animate in
  useEffect(() => {
    const t = setTimeout(() => setReady(true), 80);
    return () => clearTimeout(t);
  }, []);

  const view = toBasketsView(data, adjusted);
  const scale = Math.max(...view.baskets.map((b) => Math.max(b.amount, b.limit)), 1) * 1.15;
  const leaks = view.baskets.find((b) => b.key === "leaks");
  const leaksState = leaks ? basketState(leaks) : "green";
  const selected = view.baskets.find((b) => b.key === open) ?? view.baskets[0];
  const selectedState = basketState(selected);
  const isReal = !Array.isArray(data.baskets);

  return (
    <div className="viz">
      <div className="viz-head">
        <div><div className="muted">Income</div><b>{eur(view.income)}</b></div>
        <div style={{ textAlign: "right" }}><div className="muted">Spent</div><b>{eur(view.spent)}</b></div>
      </div>

      <div className="tanks">
        {view.baskets.map((b) => {
          const state = basketState(b);
          const over = b.amount > b.limit;
          return (
            <button
              key={b.key}
              className={"tank-col" + (open === b.key ? " selected" : "")}
              onClick={() => setOpen(b.key)}
            >
              <div className="tank">
                <div
                  className={"tank-fill " + state}
                  style={{ height: ready ? `${(b.amount / scale) * 100}%` : "0%" }}
                />
                <div
                  className={"limit" + (over ? " over" : "")}
                  style={{ bottom: `${(b.limit / scale) * 100}%` }}
                />
              </div>
              <div className="tank-amount">{eur(b.amount)}</div>
              <div className={"tank-pct " + state}>
                {over ? `+${eur(b.amount - b.limit)} over` : `${Math.round((b.amount / b.limit) * 100)}% of limit`}
              </div>
              <div className="tank-label">{b.label}</div>
            </button>
          );
        })}
      </div>
      <div className="legend">Dashed line = safe limit. Tap a tank to see what is inside.</div>
      {isReal && (
        <div className="legend">Limits are estimated from your income and expected irregular costs.</div>
      )}

      {leaks && leaksState !== "green" && data.runOutLabel && (
        <div className={"countdown " + leaksState}>
          At this pace you run out on the {data.runOutLabel}.
        </div>
      )}

      <div className="inside">
        <div className="inside-title">Inside: {selected.label}</div>
        {selected.items?.length ? (
          selected.items.map((it) => (
            <div className="inside-row" key={it.name}>
              <div className="inside-top"><span>{it.name}</span><b>{eur(it.amount)}</b></div>
              <div className="bar">
                <div
                  className={"bar-fill " + selectedState}
                  style={{ width: `${selected.amount ? (it.amount / selected.amount) * 100 : 0}%` }}
                />
              </div>
            </div>
          ))
        ) : (
          <div className="muted">No breakdown available for this basket yet.</div>
        )}
      </div>

      {data.irregular_estimate && (
        <IrregularAdjust
          average={data.irregular_estimate.average_per_month}
          adjusted={adjusted}
          onSaved={setAdjusted}
        />
      )}
    </div>
  );
}

/* ---------- 2. Comparison ---------- */
function toComparisonView(data) {
  if (data.this_month !== undefined) return data; // mock shape
  return {
    this_month: Number(data.current.expenses),
    last_month: Number(data.previous.expenses),
    items: [],
    unusual: null,
    labels: ["This month so far", "Same days last month"],
  };
}

function Comparison({ data }) {
  const v = toComparisonView(data);
  const items = v.items?.length
    ? v.items
    : [{ name: "Total spending", this: v.this_month, last: v.last_month }];
  const max = Math.max(...items.flatMap((i) => [i.this, i.last]), 1);
  const [l1, l2] = v.labels ?? ["This month", "Last month"];
  const diff = v.this_month - v.last_month;
  const pct = v.last_month > 0 ? (diff / v.last_month) * 100 : null;

  return (
    <div className="viz">
      <div className="viz-head">
        <div><div className="muted">{l1}</div><b>{eur(v.this_month)}</b></div>
        <div style={{ textAlign: "right" }}><div className="muted">{l2}</div><b>{eur(v.last_month)}</b></div>
      </div>
      {items.map((i) => {
        const d = i.this - i.last;
        return (
          <div className="cmp-row" key={i.name}>
            <div className="cmp-top">
              <span>{i.name}</span>
              <span className={d > 0 ? "up" : "down"}>{d > 0 ? "+" : ""}{eur(d)}</span>
            </div>
            <div className="bar"><div className="bar-fill now" style={{ width: `${(i.this / max) * 100}%` }} /></div>
            <div className="bar"><div className="bar-fill prev" style={{ width: `${(i.last / max) * 100}%` }} /></div>
          </div>
        );
      })}
      <div className="legend">Dark = {l1.toLowerCase()}, light = {l2.toLowerCase()}</div>
      {pct !== null && (
        <div className="note">
          Spending is {diff >= 0 ? "up" : "down"} {eur(Math.abs(diff))} ({Math.abs(pct).toFixed(0)}%).
        </div>
      )}
      {v.unusual && <div className="note">Unusual: {v.unusual.label}, {eur(v.unusual.amount)}</div>}
    </div>
  );
}

/* ---------- 3. Leaks ---------- */
function toLeaksView(data) {
  const items = data.items
    .map((i) =>
      "transaction_count" in i
        ? { name: i.category || i.merchant, count: i.transaction_count, amount: Number(i.total_amount), avg: Number(i.average_transaction) }
        : i
    )
    .sort((a, b) => b.amount - a.amount)
    .slice(0, 8);
  return { total: Number(data.total), items };
}

function Leaks({ data }) {
  const v = toLeaksView(data);
  const totalSave = v.items.reduce((s, i) => s + (i.save || 0), 0);
  return (
    <div className="viz">
      {v.items.map((i, idx) => (
        <div className="leak-row" key={i.name + idx}>
          <div className="leak-rank">{idx + 1}</div>
          <div className="leak-main">
            <div><b>{i.name}</b></div>
            <div className="muted">{i.count} purchases, {eur(i.amount)}</div>
          </div>
          <div className="leak-save">
            {i.save != null ? `save ${eur(i.save)}` : i.avg != null ? `avg ${eur1(i.avg)}` : ""}
          </div>
        </div>
      ))}
      <div className="note">
        {totalSave > 0
          ? `You could keep about ${eur(totalSave)} a month.`
          : `Everyday leaks in this period: ${eur(v.total)}.`}
      </div>
    </div>
  );
}

/* ---------- 4. Safe to spend ---------- */
function toSafeView(data) {
  if ("safe_to_spend_now" in data) {
    const m = data.month_end_projection ?? {};
    const until = data.planning_until ? String(data.planning_until).slice(0, 10) : null;
    return {
      balance: data.current_balance ?? null,
      safe: data.safe_to_spend_now ?? null,
      rows: [
        ["Upcoming regular payments", -Number(data.upcoming_regular_expenses)],
        ["Planned events", -Number(data.planned_events)],
        ["Irregular reserve", -Number(data.irregular_reserve)],
        ["Safety buffer", -Number(data.safety_buffer)],
        ["Savings goal", -Number(data.savings_goal)],
      ],
      daysLeft: until 
      ? Math.max(data.balance_as_of ? daysBetween(String(data.balance_as_of).slice(0, 10), until) : daysUntil(until), 1)
        : 30,
      projected: m.projected_balance ?? null,
      advice: data.advice ?? [],
    };
  }
  // mock shape
  const eventsTotal = data.events.reduce((s, e) => s + e.amount, 0);
  return {
    balance: data.balance,
    safe: data.balance - data.bills - data.savings - data.buffer - eventsTotal,
    rows: [
      ["Upcoming bills", -data.bills],
      ["Savings commitment", -data.savings],
      ["Safety buffer", -data.buffer],
      ...data.events.map((e) => [`${e.name} (${e.date})`, -e.amount]),
    ],
    daysLeft: data.days_left,
    projected: null,
    advice: [],
  };
}

function SafeToSpend({ data }) {
  const v = toSafeView(data);
  const hasSafe = v.safe !== null && v.safe !== undefined;
  const safe = hasSafe ? Number(v.safe) : 0;
  const balance = v.balance === null || v.balance === undefined ? null : Number(v.balance);
  const ratio = balance && balance > 0 ? safe / balance : 0;
  const state = !hasSafe || safe <= 0 ? "red" : ratio >= 0.25 ? "green" : ratio >= 0.1 ? "amber" : "red";
  const pct = Math.min(1, Math.max(0, ratio / 0.4));
  const arc = Math.PI * 50;
  const perDay = Math.round(Math.max(0, safe) / v.daysLeft);

  return (
    <div className="viz">
      <div className="gauge">
        <svg viewBox="0 0 120 70">
          <path d="M 10 60 A 50 50 0 0 1 110 60" className="gauge-bg" />
          <path
            d="M 10 60 A 50 50 0 0 1 110 60"
            className={"gauge-fg " + state}
            strokeDasharray={`${pct * arc} ${arc}`}
          />
        </svg>
        <div className="gauge-value">{hasSafe ? eur(Math.max(0, safe)) : "-"}</div>
        <div className="muted">
          {hasSafe ? `safe to spend, about ${perDay} € a day` : "not enough data yet"}
        </div>
      </div>

      <div className="row"><span>Balance</span><span>{balance === null ? "-" : eur(balance)}</span></div>
      {v.rows.map(([label, value]) => (
        <div className="row" key={label}><span>{label}</span><span>{signed(Number(value))}</span></div>
      ))}
      {v.projected !== null && (
        <div className="row strong"><span>Expected at month end</span><span>{eur(v.projected)}</span></div>
      )}
      {v.advice.map((a, i) => (
        <div className="note" key={i}>{a}</div>
      ))}
    </div>
  );
}

/* ---------- 5. What if ---------- */
function WhatIf({ data }) {
  const live = data.monthly !== undefined; // real backend result
  const [rest, setRest] = useState(20);
  const [shop, setShop] = useState(30);
  const [save, setSave] = useState(100);
  const [remote, setRemote] = useState(live ? data : null);
  const [err, setErr] = useState("");

  // With the real backend, the server does the maths each time a slider moves
  useEffect(() => {
    if (!live) return;
    const t = setTimeout(async () => {
      try {
        const r = await whatIf({ restaurants_pct: rest, shopping_eur: shop, extra_savings_eur: save });
        setRemote(r);
        setErr("");
      } catch (e) {
        setErr(e.message);
      }
    }, 300);
    return () => clearTimeout(t);
  }, [live, rest, shop, save]);

  const monthly = live
    ? Number(remote?.monthly ?? 0)
    : Math.round((data.restaurants * rest) / 100 + shop + save);
  const m6 = live ? Number(remote?.in6_months ?? remote?.in_6_months ?? monthly * 6) : monthly * 6;
  const m12 = live ? Number(remote?.in12_months ?? remote?.in_12_months ?? monthly * 12) : monthly * 12;

  return (
    <div className="viz">
      <label className="slider">
        <span>Restaurants -{rest}%</span>
        <input type="range" min="0" max="50" value={rest} onChange={(e) => setRest(+e.target.value)} />
      </label>
      <label className="slider">
        <span>Shopping -{shop} €</span>
        <input type="range" min="0" max="100" step="10" value={shop} onChange={(e) => setShop(+e.target.value)} />
      </label>
      <label className="slider">
        <span>Savings +{save} €</span>
        <input type="range" min="0" max="300" step="10" value={save} onChange={(e) => setSave(+e.target.value)} />
      </label>
      <div className="whatif-result">
        <div><div className="stat-value">{eur(monthly)}</div><div className="muted">per month</div></div>
        <div><div className="stat-value">{eur(m6)}</div><div className="muted">in 6 months</div></div>
        <div><div className="stat-value">{eur(m12)}</div><div className="muted">in a year</div></div>
      </div>
      {err && <div className="form-error" style={{ marginTop: 8 }}>{err}</div>}
    </div>
  );
}

/* ---------- Picks the right card ---------- */
export default function AnswerCard({ type, data }) {
  if (!data) return null;
  switch (type) {
    case "baskets": return <Baskets data={data} />;
    case "comparison": return <Comparison data={data} />;
    case "leaks": return <Leaks data={data} />;
    case "safe_to_spend": return <SafeToSpend data={data} />;
    case "what_if": return <WhatIf data={data} />;
    default: return null;
  }
}