import { Coffee, Lock, PiggyBank, Scale, TrendingUp } from "lucide-react";

const TYPES = {
  over: {
    name: "Over the line",
    Icon: TrendingUp,
    color: "red",
    desc: "You spent more than you earned in this period.",
    tips: {
      gentle: "It happens. Start with the everyday leaks basket, because small changes add up fast.",
      honest: "You are spending more than you earn. Cut the leaks first, then review the irregular costs.",
      roast: "Your spending is ahead of your income. Bold. Start with the leaks.",
    },
  },
  coffee: {
    name: "Coffee Drifter",
    Icon: Coffee,
    color: "amber",
    desc: "Small everyday purchases take a big bite of your income.",
    tips: {
      gentle: "Nothing is wrong with a coffee. Pick one or two habits to trim and see what changes.",
      honest: "Everyday leaks take a big share of your income, and that is the easiest part to change.",
      roast: "Your coffees have their own tank and it is filling up. Try the what-if sliders.",
    },
  },
  saver: {
    name: "Steady Saver",
    Icon: PiggyBank,
    color: "green",
    desc: "You keep a healthy part of your income and your small spending is under control.",
    tips: {
      gentle: "Nice work. Consider moving part of what is left into savings.",
      honest: "Good balance. Put the money you do not spend to work in a savings goal.",
      roast: "These numbers are almost suspiciously tidy. Keep it up.",
    },
  },
  locked: {
    name: "Locked-in Planner",
    Icon: Lock,
    color: "amber",
    desc: "Most of your money is already committed to fixed costs.",
    tips: {
      gentle: "Fixed costs are heavy, so look at them once: contracts, subscriptions, insurance.",
      honest: "Over half of your income goes to fixed payments. Review contracts and subscriptions.",
      roast: "Your rent and bills got there first. Check which contracts you can renegotiate.",
    },
  },
  balanced: {
    name: "Balanced Spender",
    Icon: Scale,
    color: "green",
    desc: "Your spending is spread evenly across the three baskets.",
    tips: {
      gentle: "A healthy mix. Keep an eye on the leaks basket so it stays that way.",
      honest: "No big red flags. Watch the leaks basket and keep a buffer for irregular costs.",
      roast: "Annoyingly reasonable. I will keep looking for leaks.",
    },
  },
};

// Works with both the mock shape (baskets is a list) and the real backend shape
function totals(data) {
  const income = Number(data.income) || 0;
  const pick = (key) =>
    Array.isArray(data.baskets)
      ? Number(data.baskets.find((b) => b.key === key)?.amount) || 0
      : Number(data.baskets?.[key]) || 0;
  const regular = pick("regular");
  const irregular = pick("irregular");
  const leaks = pick("leaks");
  const spent = Number(data.expenses ?? data.spent) || regular + irregular + leaks;
  return { income, spent, regular, irregular, leaks };
}

function pickType({ income, spent, regular, leaks }) {
  if (income <= 0) return null;
  const leaksShare = leaks / income;
  const fixedShare = regular / income;
  const leftShare = (income - spent) / income;
  if (spent > income) return "over";
  if (leaksShare >= 0.15) return "coffee";
  if (leftShare >= 0.2 && leaksShare < 0.1) return "saver";
  if (fixedShare >= 0.6) return "locked";
  return "balanced";
}

const share = (n, income) => Math.min(100, Math.max(0, Math.round((n / income) * 100)));

export default function SpenderType({ data, tone }) {
  const t = totals(data);
  const key = pickType(t);
  if (!key) return null;
  const type = TYPES[key];

  const rows = [
    ["Fixed costs", t.regular, "now"],
    ["Irregular costs", t.irregular, "amber"],
    ["Everyday leaks", t.leaks, "red"],
    ["Left over", Math.max(0, t.income - t.spent), "green"],
  ];

  return (
    <div className="viz">
      <div className="spender-head">
        <span className={"spender-icon " + type.color}>
          <type.Icon size={26} />
        </span>
        <div>
          <div className="muted">What kind of spender am I?</div>
          <div className="spender-name">{type.name}</div>
        </div>
      </div>
      <p className="spender-desc">{type.desc}</p>

      {rows.map(([label, amount, cls]) => (
        <div className="meter" key={label}>
          <div className="meter-top">
            <span>{label}</span>
            <b>{share(amount, t.income)}% of income</b>
          </div>
          <div className="bar">
            <div className={"bar-fill " + cls} style={{ width: `${share(amount, t.income)}%` }} />
          </div>
        </div>
      ))}

      <div className="note">{type.tips[tone] ?? type.tips.honest}</div>
      <div className="legend">A fun guide based on this period's income and spending, not financial advice.</div>
    </div>
  );
}