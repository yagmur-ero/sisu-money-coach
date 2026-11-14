import { MOCK, HOME } from "./mockData";
import { formatDate, daysUntil } from "./dates";

const COACH_URL = import.meta.env.VITE_COACH_URL ?? "/coach";

// Days between two "YYYY-MM-DD" dates (count from the backend's date, not the device clock)
export function daysBetween(fromIso, toIso) {
  return Math.round((new Date(toIso + "T00:00:00") - new Date(fromIso + "T00:00:00")) / 86400000);
}

// Real backend: put VITE_API_URL=http://localhost:5080 (or /api with the Vite proxy) in .env
const API_URL = import.meta.env.VITE_API_URL;
export const USE_API = Boolean(API_URL);

async function request(path, options = {}) {
  const res = await fetch(`${API_URL}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    let message = `Server error ${res.status}`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch {}
    throw new Error(message);
  }
  return res.status === 204 ? null : res.json();
}

function guessQuestion(text) {
  const t = text.toLowerCase();
  if (/afford|safe|trip|buy/.test(t)) return 4;
  if (/leak|coffee|small|waste/.test(t)) return 3;
  if (/last month|change|more than|increase/.test(t)) return 2;
  if (/what if|cut|reduce|save/.test(t)) return 5;
  return 1;
}

// Mock-mode safe-to-spend text, built from the planned expenses
function safeAnswer(d, tone) {
  const planned = d.events.reduce((s, e) => s + e.amount, 0);
  const safe = Math.max(0, d.balance - d.bills - d.savings - d.buffer - planned);
  const perDay = Math.round(safe / d.days_left);
  if (safe === 0) return "You have nothing safe to spend right now. Wait for payday.";
  if (tone === "gentle")
    return `After bills, savings, your buffer and planned expenses you can safely spend about ${safe} EUR until payday.`;
  if (tone === "roast") return `${perDay} EUR a day. That is one coffee and a stern look.`;
  return `Safe to spend: ${safe} EUR over ${d.days_left} days, about ${perDay} EUR a day.`;
}

/* ---------- Planned events ---------- */
const toEvent = (e) => ({
  id: e.id,
  name: e.name,
  date: String(e.date).slice(0, 10),
  amount: Number(e.amount),
});

export async function listEvents() {
  const list = await request("/events");
  return list.map(toEvent);
}

export async function createEvent(ev) {
  const created = await request("/events", {
    method: "POST",
    body: JSON.stringify({ name: ev.name, date: ev.date, amount: ev.amount }),
  });
  return toEvent(created);
}

export async function deleteEvent(id) {
  await request(`/events/${id}`, { method: "DELETE" });
}

/* ---------- Plan and what-if ---------- */
export function savePlan(fields) {
  return request("/plan", { method: "PUT", body: JSON.stringify(fields) });
}

export function whatIf(values) {
  return request("/insights/what-if", { method: "POST", body: JSON.stringify(values) });
}

/* ---------- Main page overview ---------- */
function isoDaysBefore(iso, n) {
  const d = new Date(iso + "T12:00:00");
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

export async function getHome({ events = [] } = {}) {
  if (USE_API) {
    const [accounts, safe, baskets] = await Promise.all([
      request("/accounts"),
      request("/insights/safe-to-spend"),
      request("/insights/baskets"),
    ]);

    // Recent transactions near the data cutoff (the dataset may end before today)
    let recent = [];
    const asOf = safe.balance_as_of ? String(safe.balance_as_of).slice(0, 10) : null;
    if (asOf) {
      try {
        const tx = await request(`/transactions?from=${isoDaysBefore(asOf, 14)}&to=${asOf}`);
        recent = tx
          .sort((a, b) => String(b.date).localeCompare(String(a.date)) || b.id - a.id)
          .slice(0, 5)
          .map((t) => ({
            id: t.id,
            date: String(t.date).slice(0, 10),
            title: String(t.merchant || t.category || "Transaction"),
            category: t.category || "",
            amount: Number(t.amount),
          }));
      } catch {}
    }

    const balance = safe.current_balance ?? accounts.reduce((s, a) => s + Number(a.balance), 0);
    const proj = safe.month_end_projection ?? {};
    const monthly = Math.max(
      Math.max(Number(proj.expected_monthly_income) - Number(safe.savings_goal), 0) ||
        Number(proj.average_monthly_expenses) ||
        0,
      1
    );
    const until = safe.planning_until ? String(safe.planning_until).slice(0, 10) : null;

    return {
      balance: Number(balance),
      accounts: accounts.map((a) => ({ id: a.id, name: a.name, balance: Number(a.balance) })),
      recent,
      month: { income: Number(baskets.income), expenses: Number(baskets.expenses) },
      budget: {
        monthly,
        safe_to_spend: Number(safe.safe_to_spend_now ?? 0),
        days_left: until ? Math.max(asOf ? daysBetween(asOf, until) : daysUntil(until), 1) : 30,
      },
    };
  }

  await new Promise((r) => setTimeout(r, 300));
  const d = MOCK[4].data;
  const planned = events
    .filter((e) => daysUntil(e.date) >= 0 && daysUntil(e.date) <= d.days_left)
    .reduce((s, e) => s + e.amount, 0);
  const safe = Math.max(0, d.balance - d.bills - d.savings - d.buffer - planned);
  return { ...HOME, budget: { ...HOME.budget, safe_to_spend: safe, days_left: d.days_left } };
}

async function askSisuCoach({ text, tone, history }) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 20000);
  try {
    const res = await fetch(`${COACH_URL}/advice`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      signal: controller.signal,
      body: JSON.stringify({
        customer_id: "CUST00001",
        message: text,
        tone,
        history: history
          .filter((m) => m.text)
          .slice(-6)
          .map((m) => ({ role: m.from === "me" ? "user" : "assistant", content: String(m.text).slice(0, 2000) })),
      }),
    });
    if (!res.ok) throw new Error(`Coach error ${res.status}`);
    const data = await res.json();
    return { type: null, answer: data.reply, data: null };
  } finally {
    clearTimeout(timer);
  }
}

/* ---------- Chat ---------- */
// Returns { type, answer, data }
export async function askCoach({ questionId, text, tone, events = [], history=[] }) {
  if (USE_API) {
    if (questionId == null) {
      try {
        return await askSisuCoach({ text, tone, history });
      } catch {}
    }

    const res = await request("/ask", {
      method: "POST",
      body: JSON.stringify({ question_id: questionId ?? null, text, tone }),
    });
    // The basket answer has no item lists, so fetch the leaks breakdown for the leaks tank
    if (res.type === "baskets") {
      try {
        const leaks = await request("/insights/leaks");
        res.data = { ...res.data, leak_items: leaks.items };
      } catch {}
    }
    return res;
  }

  await new Promise((r) => setTimeout(r, 700));
  const m = MOCK[questionId ?? guessQuestion(text)];

  if (m.type === "safe_to_spend") {
    const data = {
      ...m.data,
      events: events
        .filter((e) => daysUntil(e.date) >= 0 && daysUntil(e.date) <= m.data.days_left)
        .map((e) => ({ name: e.name, date: formatDate(e.date), amount: e.amount })),
    };
    return { type: m.type, answer: safeAnswer(data, tone), data };
  }

  return { type: m.type, answer: m.answers[tone] ?? m.answers.honest, data: m.data };
}
// Data for the Baskets page (same data as the baskets answer card)
export async function getBaskets() {
  if (USE_API) {
    const data = await request("/insights/baskets");
    try {
      const leaks = await request("/insights/leaks");
      return { ...data, leak_items: leaks.items };
    } catch {
      return data;
    }
  }
  await new Promise((r) => setTimeout(r, 300));
  return MOCK[1].data;
}