"""
Sisu Coach - a warm, user-first money coach.

How it works
1. Reads the customer's past transactions from MySQL (spankki.transactions).
2. Machine learning on that history:
   - KMeans clustering over all customers gives each person a "spending personality".
   - A weighted moving-average forecast predicts next month's spending per category.
3. An LLM turns those facts into friendly, human advice for next month.
   It never invents numbers: every figure it may use is computed here first.
   Without an API key, a warm template answer is used instead.
"""
import json
import logging
import os
import threading
from collections import defaultdict
from contextlib import asynccontextmanager
from datetime import date

import numpy as np
import pymysql
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from sklearn.cluster import KMeans
from sklearn.preprocessing import StandardScaler

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("coach")

# The dataset ends on 2026-09-30, so "today" is fixed for the demo.
DEMO_TODAY = date.fromisoformat(os.getenv("DEMO_TODAY", "2026-09-30"))
HISTORY_MONTHS = 6
SMALL_PURCHASE_EUR = 20.0
AI_MODEL = os.getenv("AI_MODEL", "gpt-4.1-mini")
# Any OpenAI-compatible provider (e.g. Groq: https://api.groq.com/openai/v1). Empty = OpenAI.
AI_BASE_URL = os.getenv("AI_BASE_URL") or None

INCOME_TYPES = ("salary", "other_income")
ESSENTIAL_TYPES = ("housing_payment", "utilities_payment")
# Spending the coach must never nudge people to cut (it may be a real need).
CARE_CATEGORIES = {"Health"}
# investment_deposit, investment_withdrawal and salary_sweep move money between the
# customer's own accounts, so they count as transfers: not spending, not income.


# ---------------------------------------------------------------- database

def connect():
    password = os.getenv("DB_PASSWORD")
    if not password:
        raise RuntimeError("DB_PASSWORD environment variable is not set.")
    return pymysql.connect(
        host=os.getenv("DB_HOST", "localhost"),
        port=int(os.getenv("DB_PORT", "3307")),
        user=os.getenv("DB_USER", "root"),
        password=password,
        database=os.getenv("DB_NAME", "spankki"),
        cursorclass=pymysql.cursors.DictCursor,
        connect_timeout=5,
    )


def query(sql, params=()):
    with connect() as conn, conn.cursor() as cur:
        cur.execute(sql, params)
        return cur.fetchall()


def month_keys(end, count):
    """['2026-04', ..., '2026-09'] for end=2026-09-30, count=6."""
    year, month = end.year, end.month
    keys = []
    for _ in range(count):
        keys.append(f"{year:04d}-{month:02d}")
        month -= 1
        if month == 0:
            year, month = year - 1, 12
    return keys[::-1]


def window():
    months = month_keys(DEMO_TODAY, HISTORY_MONTHS)
    start = date.fromisoformat(months[0] + "-01")
    return months, start, DEMO_TODAY


# ------------------------------------------------- ML 1: spending personality

PERSONALITIES = [
    # (feature index, name, description) - assigned to the cluster that scores highest
    (0, "Careful Saver",
     "you naturally set money aside - future you is already thankful"),
    (1, "Little Treats Lover",
     "lots of small, happy purchases - coffees, snacks and little joys add up"),
    (2, "Spontaneous Spender",
     "your spending moves in waves - some months calm, some months busy"),
    (3, "Essentials First",
     "most of your money goes to the must-haves like home and bills"),
]
DEFAULT_PERSONALITY = ("Balanced Planner",
                       "a fairly even mix - no big surprises in your spending")
FEATURES = ["savings_rate", "small_purchase_share", "volatility", "essentials_share", "avg_purchase_eur"]


class PersonalityModel:
    """Unsupervised clustering of all customers on five behaviour features."""

    def __init__(self, n_clusters=5):
        self.n_clusters = n_clusters
        self.labels = {}
        self.names = {}
        self.ready = False
        self._lock = threading.Lock()

    def ensure_fitted(self):
        with self._lock:
            if not self.ready:
                self._fit()
                self.ready = True

    def _fit(self):
        months, start, end = window()
        log.info("Fitting personality model on %s .. %s", start, end)
        totals = query(
            """
            SELECT customer_id,
                   SUM(CASE WHEN transaction_type IN ('salary','other_income') THEN tx_amount_eur ELSE 0 END) AS income,
                   SUM(CASE WHEN transaction_type IN ('purchase','utilities_payment','housing_payment') THEN -tx_amount_eur ELSE 0 END) AS expenses,
                   SUM(CASE WHEN transaction_type IN ('utilities_payment','housing_payment') THEN -tx_amount_eur ELSE 0 END) AS essentials,
                   SUM(transaction_type = 'purchase') AS purchase_n,
                   SUM(transaction_type = 'purchase' AND tx_amount_eur > -%s) AS small_n,
                   SUM(CASE WHEN transaction_type = 'purchase' THEN -tx_amount_eur ELSE 0 END) AS purchase_sum,
                   SUM(CASE WHEN transaction_type = 'investment_deposit' THEN -tx_amount_eur ELSE 0 END) AS invested
            FROM transactions
            WHERE tx_date BETWEEN %s AND %s
            GROUP BY customer_id
            """,
            (SMALL_PURCHASE_EUR, start, end),
        )
        monthly = query(
            """
            SELECT customer_id, DATE_FORMAT(tx_date, '%%Y-%%m') AS ym, SUM(-tx_amount_eur) AS spent
            FROM transactions
            WHERE tx_date BETWEEN %s AND %s
              AND transaction_type IN ('purchase','utilities_payment','housing_payment')
            GROUP BY customer_id, ym
            """,
            (start, end),
        )
        spent_by_month = defaultdict(list)
        for row in monthly:
            spent_by_month[row["customer_id"]].append(float(row["spent"]))

        ids, rows = [], []
        for row in totals:
            income = float(row["income"] or 0)
            expenses = float(row["expenses"] or 0)
            purchases = float(row["purchase_n"] or 0)
            spent = spent_by_month.get(row["customer_id"], [])
            mean_spent = float(np.mean(spent)) if spent else 0.0
            ids.append(row["customer_id"])
            rows.append([
                min(max(float(row["invested"] or 0) / income, 0.0), 1.0) if income > 0 else 0.0,
                float(row["small_n"] or 0) / purchases if purchases else 0.0,
                float(np.std(spent)) / mean_spent if mean_spent > 0 else 0.0,
                float(row["essentials"] or 0) / expenses if expenses > 0 else 0.0,
                float(row["purchase_sum"] or 0) / purchases if purchases else 0.0,
            ])
        if len(rows) < self.n_clusters:
            raise RuntimeError("Not enough customers to build personalities.")

        scaled = StandardScaler().fit_transform(np.array(rows))
        kmeans = KMeans(n_clusters=self.n_clusters, n_init=10, random_state=42).fit(scaled)
        self.labels = dict(zip(ids, kmeans.labels_.tolist()))

        # Name each cluster by the trait it is strongest in (centres are z-scores).
        free = set(range(self.n_clusters))
        for feature_index, name, description in PERSONALITIES:
            best = max(free, key=lambda c: kmeans.cluster_centers_[c][feature_index])
            self.names[best] = (name, description)
            free.remove(best)
        for cluster in free:
            self.names[cluster] = DEFAULT_PERSONALITY
        log.info("Personality model ready: %d customers, %d clusters", len(ids), self.n_clusters)

    def personality_of(self, customer_id):
        self.ensure_fitted()
        cluster = self.labels.get(customer_id)
        name, description = self.names.get(cluster, DEFAULT_PERSONALITY)
        return {"name": name, "description": description}


personality_model = PersonalityModel()


# ----------------------------------------------------- ML 2: spending forecast

def forecast(values):
    """Weighted moving average of the last 3 months (recent months count more),
    plus the trend versus the 3 months before."""
    last3 = values[-3:]
    prev3 = values[-6:-3]
    predicted = float(np.dot([0.2, 0.3, 0.5], last3))
    prev_avg = float(np.mean(prev3)) if prev3 else 0.0
    trend = (float(np.mean(last3)) - prev_avg) / prev_avg * 100 if prev_avg > 0 else 0.0
    return round(predicted, 2), round(trend, 1)


def build_facts(customer_id):
    months, start, end = window()
    rows = query(
        """
        SELECT DATE_FORMAT(tx_date, '%%Y-%%m') AS ym, transaction_type, category,
               SUM(tx_amount_eur) AS total,
               SUM(transaction_type = 'purchase' AND tx_amount_eur > -%s) AS small_n,
               SUM(CASE WHEN transaction_type = 'purchase' AND tx_amount_eur > -%s
                        THEN -tx_amount_eur ELSE 0 END) AS small_sum
        FROM transactions
        WHERE customer_id = %s AND tx_date BETWEEN %s AND %s
        GROUP BY ym, transaction_type, category
        """,
        (SMALL_PURCHASE_EUR, SMALL_PURCHASE_EUR, customer_id, start, end),
    )
    if not rows:
        raise HTTPException(404, f"No transactions found for customer {customer_id}.")

    income = dict.fromkeys(months, 0.0)
    essentials = dict.fromkeys(months, 0.0)
    invested = dict.fromkeys(months, 0.0)
    small_count = dict.fromkeys(months, 0.0)
    small_eur = dict.fromkeys(months, 0.0)
    flexible = defaultdict(lambda: dict.fromkeys(months, 0.0))

    for row in rows:
        month, kind, amount = row["ym"], row["transaction_type"], float(row["total"])
        if month not in income:
            continue
        if kind in INCOME_TYPES:
            income[month] += amount
        elif kind in ESSENTIAL_TYPES:
            essentials[month] += -amount
        elif kind == "purchase":
            flexible[row["category"] or "Other"][month] += -amount
            small_count[month] += float(row["small_n"] or 0)
            small_eur[month] += float(row["small_sum"] or 0)
        elif kind == "investment_deposit":
            invested[month] += -amount

    def series(by_month):
        return [by_month[m] for m in months]

    essentials_forecast, essentials_trend = forecast(series(essentials))
    categories = {}
    for category, by_month in flexible.items():
        predicted, trend = forecast(series(by_month))
        categories[category] = {
            "avg_per_month": round(float(np.mean(series(by_month))), 2),
            "forecast_next_month": predicted,
            "trend_pct_vs_previous_3_months": trend,
        }
    flexible_forecast = round(sum(c["forecast_next_month"] for c in categories.values()), 2)
    total_forecast = round(essentials_forecast + flexible_forecast, 2)
    avg_income = round(float(np.mean(series(income))), 2)
    room = round(avg_income - total_forecast, 2)
    gentle_saving = round(max(room, 0) * 0.3, 2)
    weekly_flexible = round(max(avg_income - essentials_forecast - gentle_saving, 0) / 4.33, 2)

    top = sorted(categories.items(), key=lambda kv: kv[1]["forecast_next_month"], reverse=True)[:3]
    rising = [(name, c) for name, c in categories.items()
              if c["trend_pct_vs_previous_3_months"] > 10 and c["avg_per_month"] > 20
              and name not in CARE_CATEGORIES]
    rising.sort(key=lambda kv: kv[1]["trend_pct_vs_previous_3_months"], reverse=True)

    balance_row = query(
        """
        SELECT SUM(balance_after_eur) AS balance FROM (
            SELECT balance_after_eur,
                   ROW_NUMBER() OVER (PARTITION BY bank_account_id ORDER BY tx_timestamp DESC, id DESC) AS rn
            FROM transactions WHERE customer_id = %s AND tx_date <= %s
        ) latest WHERE rn = 1
        """,
        (customer_id, end),
    )

    return {
        "customer_id": customer_id,
        "as_of": DEMO_TODAY.isoformat(),
        "months_analysed": months,
        "personality": personality_model.personality_of(customer_id),
        "current_balance_eur": round(float(balance_row[0]["balance"] or 0), 2),
        "avg_monthly_income_eur": avg_income,
        "avg_monthly_essentials_eur": round(float(np.mean(series(essentials))), 2),
        "avg_monthly_flexible_eur": round(sum(c["avg_per_month"] for c in categories.values()), 2),
        "avg_monthly_saved_or_invested_eur": round(float(np.mean(series(invested))), 2),
        "next_month_forecast": {
            "essentials_eur": essentials_forecast,
            "essentials_trend_pct": essentials_trend,
            "flexible_eur": flexible_forecast,
            "total_eur": total_forecast,
            "room_left_after_forecast_eur": room,
        },
        "top_flexible_categories": [{"category": n, **c} for n, c in top],
        "rising_categories": [{"category": n, **c} for n, c in rising[:2]],
        "small_purchases_under_20_eur": {
            "avg_count_per_month": round(float(np.mean(series(small_count))), 1),
            "avg_eur_per_month": round(float(np.mean(series(small_eur))), 2),
        },
        "suggested_gentle_saving_next_month_eur": gentle_saving,
        "suggested_weekly_flexible_budget_eur": weekly_flexible,
    }


# ------------------------------------------------------------ human-like coach

SYSTEM_PROMPT = """You are Sisu, a warm personal money coach inside a banking app.
Your only goal is the user's wellbeing, never the bank's sales.

How you talk:
- Like a caring friend who happens to be good with money: warm, encouraging and human.
- Show real empathy. Notice feelings ("that sounds like a stressful month"), celebrate wins ("nice work!"), never shame or lecture.
- Speak to the person, not about data. No tables, no bullet-point reports, at most one short list.
- Keep it short: about 120 words. One or two friendly emojis are fine.
- Reply in the same language the user writes in.

What you do:
- Look at their past spending patterns and the forecast for next month, then suggest 2-3 small, concrete, realistic steps that make next month feel easier.
- Use their spending personality to make the advice feel personal.

Hard rules:
- Use ONLY numbers that appear in FACTS. Never invent, estimate or recalculate figures. If something is not in FACTS, say you can't see it yet.
- Never recommend or mention loans, credit cards, investment products, insurance or any other bank offer. No advertising of any kind.
- Never suggest cutting health or medical spending; it may be a real need. You may acknowledge it kindly.
- You are not a licensed financial advisor. For big decisions, gently suggest talking to a professional."""


def ask_llm(facts, message, history):
    api_key = os.getenv("OPENAI_API_KEY")
    if not api_key:
        return None
    try:
        from openai import OpenAI

        client = OpenAI(api_key=api_key, base_url=AI_BASE_URL, timeout=20)
        messages = [{"role": "system", "content": SYSTEM_PROMPT}]
        for turn in history[-6:]:
            if turn.role in ("user", "assistant") and turn.content.strip():
                messages.append({"role": turn.role, "content": turn.content})
        messages.append({
            "role": "user",
            "content": f"FACTS (JSON, as of {facts['as_of']}):\n{json.dumps(facts)}\n\nMy message: {message}",
        })
        response = client.chat.completions.create(
            # Reasoning models (e.g. gpt-oss on Groq) spend tokens thinking before answering,
            # so leave room; the system prompt still keeps the reply around 120 words.
            model=AI_MODEL, messages=messages, temperature=0.7, max_tokens=1500,
        )
        text = (response.choices[0].message.content or "").strip()
        return text or None
    except Exception:  # any provider problem falls back to the template coach
        log.exception("LLM call failed, using template answer")
        return None


def template_reply(facts):
    """Friendly answer built only from FACTS, used when no AI key is configured."""
    p = facts["personality"]
    f = facts["next_month_forecast"]
    small = facts["small_purchases_under_20_eur"]
    parts = [f"Hi! 😊 I had a look at your last {len(facts['months_analysed'])} months. "
             f"You're a bit of a {p['name']}: {p['description']}."]

    top = facts["top_flexible_categories"]
    if top:
        parts.append(f"Next month I expect around €{f['total_eur']:.0f} of spending, "
                     f"and {top[0]['category']} is where most of your flexible money goes.")
    if facts["rising_categories"]:
        r = facts["rising_categories"][0]
        parts.append(f"{r['category']} has been creeping up lately "
                     f"(+{r['trend_pct_vs_previous_3_months']:.0f}%), so it might be worth a gentle look.")

    if f["room_left_after_forecast_eur"] > 0:
        parts.append(f"The good news: that should leave about €{f['room_left_after_forecast_eur']:.0f}. "
                     f"How about moving €{facts['suggested_gentle_saving_next_month_eur']:.0f} aside right after payday, "
                     f"before the month gets busy?")
    else:
        parts.append("Next month looks a little tight, and that's okay. "
                     "Let's protect the essentials first and keep the extras light.")

    if small["avg_count_per_month"] >= 5:
        parts.append(f"Your small purchases add up to about €{small['avg_eur_per_month']:.0f} a month. "
                     f"Keep the ones that truly make you happy and skip a few of the rest ☕.")
    parts.append(f"A comfortable weekly budget for the flexible stuff would be about "
                 f"€{facts['suggested_weekly_flexible_budget_eur']:.0f}. You've got this! 💪")
    return " ".join(parts)


# --------------------------------------------------------------------- the API

@asynccontextmanager
async def lifespan(_app):
    # Train the personality model in the background so the API starts immediately.
    threading.Thread(target=_warm_up, daemon=True).start()
    yield


def _warm_up():
    try:
        personality_model.ensure_fitted()
    except Exception:
        log.exception("Personality model warm-up failed; it will retry on the first request")


app = FastAPI(title="Sisu Coach", lifespan=lifespan)


class Turn(BaseModel):
    role: str
    content: str


class AdviceRequest(BaseModel):
    customer_id: str = Field(default="CUST00001", max_length=32)
    message: str = Field(default="How can I plan next month?", max_length=1000)
    history: list[Turn] = Field(default_factory=list)


@app.get("/health")
def health():
    return {"status": "ok", "demo_today": DEMO_TODAY.isoformat(),
            "personality_model_ready": personality_model.ready,
            "ai_enabled": bool(os.getenv("OPENAI_API_KEY")),
            "ai_model": AI_MODEL,
            "ai_provider": AI_BASE_URL or "openai"}


@app.get("/profile/{customer_id}")
def profile(customer_id: str):
    return build_facts(customer_id)


@app.post("/advice")
def advice(request: AdviceRequest):
    facts = build_facts(request.customer_id)
    reply = ask_llm(facts, request.message, request.history)
    source = "ai"
    if reply is None:
        reply, source = template_reply(facts), "template"
    return {"reply": reply, "source": source, "facts": facts}
