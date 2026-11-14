import { useState } from "react";
import { ChevronLeft, Trash2 } from "lucide-react";
import { formatDate, daysUntil, inDays } from "../dates";

export default function Events({ events, onAdd, onRemove, onBack }) {
  const [name, setName] = useState("");
  const [date, setDate] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e) {
    e.preventDefault();
    const value = Number(String(amount).replace(",", "."));
    if (!name.trim()) return setError("Write what the expense is.");
    if (!date) return setError("Pick a date.");
    if (daysUntil(date) < 0) return setError("That date has already passed. Pick today or later.");
    if (!(value > 0)) return setError("Enter an amount above 0.");

    setBusy(true);
    const problem = await onAdd({ id: Date.now(), name: name.trim(), date, amount: value });
    setBusy(false);
    if (problem) return setError(problem);

    setName("");
    setDate("");
    setAmount("");
    setError("");
  }

  async function remove(id) {
    const problem = await onRemove(id);
    if (problem) setError(problem);
  }

  const sorted = [...events].sort((a, b) => a.date.localeCompare(b.date));
  const total = sorted
    .filter((e) => daysUntil(e.date) >= 0)
    .reduce((s, e) => s + e.amount, 0);

  return (
    <div className="phone">
      <button className="back" onClick={onBack}><ChevronLeft size={20} /> Back</button>
      <h1 className="greeting">Planned expenses</h1>
      <p className="muted">Add things you know are coming. They are taken out of your safe-to-spend.</p>

      <div className="viz">
        <div className="muted">Coming up</div>
        <div className="stat-value">{total} €</div>
      </div>

      <form className="event-form" onSubmit={submit}>
        <input
          className="text-field"
          placeholder="What is it? (e.g. Friend's gift)"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <div className="event-row-inputs">
          <input
            className="text-field"
            type="date"
            min={inDays(0)}
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
          <input
            className="text-field"
            type="text"
            inputMode="decimal"
            placeholder="Amount €"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
        </div>
        {error && <div className="form-error">{error}</div>}
        <button className="primary-btn" type="submit" disabled={busy}>
          {busy ? "Saving..." : "Add expense"}
        </button>
      </form>

      <div style={{ marginTop: 20 }}>
        {sorted.length === 0 && <p className="muted">Nothing planned yet.</p>}
        {sorted.map((e) => {
          const d = daysUntil(e.date);
          return (
            <div className={"event-item" + (d < 0 ? " past" : "")} key={e.id}>
              <div className="event-main">
                <b>{e.name}</b>
                <div className="muted">
                  {formatDate(e.date)} ·{" "}
                  {d < 0 ? "passed" : d === 0 ? "today" : d === 1 ? "tomorrow" : `in ${d} days`}
                </div>
              </div>
              <div className="event-amount">{e.amount} €</div>
              <button className="trash" onClick={() => remove(e.id)}><Trash2 size={18} /></button>
            </div>
          );
        })}
      </div>
    </div>
  );
}