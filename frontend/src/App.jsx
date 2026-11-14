import { useState, useRef, useEffect } from "react";
import { LayoutGrid, Send, CalendarDays, House, MessageCircle, Layers } from "lucide-react";
import { useLocalStorage } from "./hooks";
import { askCoach, getHome, listEvents, createEvent, deleteEvent, USE_API } from "./api";
import { QUESTIONS } from "./questions";
import { formatDate, daysUntil, inDays } from "./dates";
import Avatar from "./components/Avatar";
import Setup from "./components/Setup";
import Profile from "./components/Profile";
import Events from "./components/Events";
import Home from "./components/Home";
import BasketsPage from "./components/BasketsPage";
import AnswerCard from "./components/AnswerCard";
import "./index.css";

const TABS = [
  { id: "home", label: "Home", Icon: House },
  { id: "chat", label: "Chat", Icon: MessageCircle },
  { id: "baskets", label: "Baskets", Icon: Layers },
];

function QuestionCards({ onPick }) {
  return (
    <div className="cards">
      {QUESTIONS.map((q) => (
        <button key={q.id} className="card" onClick={() => onPick(q)}>
          <span className="icon"><q.Icon size={22} /></span>
          {q.title}
        </button>
      ))}
    </div>
  );
}

function warningText(ev, tone) {
  const when = formatDate(ev.date);
  if (tone === "gentle") return `Reminder: ${ev.name} on ${when} (${ev.amount} €). I have set it aside for you.`;
  if (tone === "roast") return `Skip the extras. You need ${ev.amount} € for ${ev.name} on ${when}.`;
  return `${ev.amount} € for ${ev.name} on ${when} is reserved. Do not spend it.`;
}

export default function App() {
  const [coach, setCoach] = useLocalStorage("coach", null);
  const [stats, setStats] = useLocalStorage("stats", { questions: 0 });
  const [events, setEvents] = useLocalStorage(
    "events",
    USE_API ? [] : [{ id: 1, name: "Friend's birthday gift", date: inDays(3), amount: 40 }]
  );
  const [view, setView] = useState("chat"); // chat | profile | setup | events (full pages)
  const [tab, setTab] = useState("home"); // home | chat | baskets
  const [messages, setMessages] = useState([]);
  const [text, setText] = useState("");
  const [menuOpen, setMenuOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [home, setHome] = useState(undefined); // undefined = loading, null = failed
  const endRef = useRef(null);

  useEffect(() => {
    if (tab === "chat") endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading, tab]);

  // With the backend on, the server list of planned expenses is the source of truth
  function refreshEvents() {
    listEvents().then(setEvents).catch(() => {});
  }
  useEffect(() => {
    if (USE_API) refreshEvents();
  }, []);

  // Load the overview, and reload when planned expenses change
  useEffect(() => {
    getHome({ events }).then(setHome).catch(() => setHome(null));
  }, [events]);

    async function addEvent(ev) {
    if (USE_API) {
      try {
        await createEvent(ev);
      } catch (e) {
        return e.message || "Could not save the expense.";
      }
      try {
        setEvents(await listEvents());
      } catch {
        return "Saved, but I could not reload the list.";
      }
      return null;
    }
    setEvents((list) => [...list, ev]);
    return null;
  }

  async function removeEvent(id) {
    if (USE_API) {
      try {
        await deleteEvent(id);
        setEvents(await listEvents());
      } catch (e) {
        return e.message || "Could not delete the expense.";
      }
      return null;
    }
    setEvents((list) => list.filter((e) => e.id !== id));
    return null;
  }

  async function send({ questionId, text }) {
    setMenuOpen(false);
    setTab("chat");
    setMessages((m) => [...m, { from: "me", text }]);
    setLoading(true);
    try {
      const res = await askCoach({ questionId, text, tone: coach.tone, events, history:messages });
      setMessages((m) => [...m, { from: "coach", text: res.answer, type: res.type, data: res.data }]);
      setStats((s) => ({ ...s, questions: s.questions + 1 }));
    } catch (err) {
      const hint =
        err.message === "Failed to fetch" ? " The backend is not reachable, or CORS is blocking it." : ` ${err.message}`;
      setMessages((m) => [...m, { from: "coach", text: "I could not get an answer." + hint }]);
    }
    setLoading(false);
  }

  function submit(e) {
    e.preventDefault();
    if (!text.trim()) return;
    send({ text });
    setText("");
  }

  if (!coach || view === "setup") {
    return (
      <Setup
        initial={coach}
        onSave={(c) => { setCoach(c); setView("chat"); }}
        onCancel={coach ? () => setView("profile") : null}
      />
    );
  }

  if (view === "profile") {
    return (
      <Profile
        coach={coach}
        stats={stats}
        onTone={(tone) => setCoach({ ...coach, tone })}
        onBack={() => setView("chat")}
        onEdit={() => setView("setup")}
      />
    );
  }

  if (view === "events") {
    return <Events events={events} onAdd={addEvent} onRemove={removeEvent} onBack={() => setView("chat")} />;
  }

  const next = events
    .filter((e) => daysUntil(e.date) >= 0 && daysUntil(e.date) <= 7)
    .sort((a, b) => a.date.localeCompare(b.date))[0];

  const pick = (q) => send({ questionId: q.id, text: q.title });

  return (
    <div className="phone chat">
      <div className="topbar">
        <button className="coach" onClick={() => setView("profile")}>
          <Avatar photo={coach.photo} name={coach.name} />
          <div>
            <div className="coach-name">{coach.name}</div>
            <div className="coach-sub">Tap for profile</div>
          </div>
        </button>
        <div className="topbar-actions">
          <button className="round" onClick={() => setView("events")} aria-label="Planned expenses">
            <CalendarDays size={20} />
          </button>
        </div>
      </div>

      {next && <div className="warning">{warningText(next, coach.tone)}</div>}

      <div className="body">
        {tab === "home" && (
          <>
            <h1 className="greeting compact">Hi, I'm {coach.name}. Here is your money today.</h1>
            <Home home={home} onAsk={(id, title) => send({ questionId: id, text: title })} />
            <h2 className="section-title">Ask your coach</h2>
            <QuestionCards onPick={pick} />
          </>
        )}

        {tab === "chat" &&
          (messages.length === 0 ? (
            <>
              <h1 className="greeting compact">What would you like to know?</h1>
              <QuestionCards onPick={pick} />
            </>
          ) : (
            <>
              {messages.map((m, i) => (
                <div key={i} className={"msg " + m.from}>
                  <div className={"bubble " + m.from}>{m.text}</div>
                  {m.type && <AnswerCard type={m.type} data={m.data} />}
                </div>
              ))}
              {loading && <div className="bubble coach typing">...</div>}
            </>
          ))}

        {tab === "baskets" && <BasketsPage tone={coach.tone} />}
        <div ref={endRef} />
      </div>

      {tab === "chat" && (
        <form className="inputbar" onSubmit={submit}>
          <button type="button" className="round" onClick={() => setMenuOpen(true)}>
            <LayoutGrid size={20} />
          </button>
          <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Ask your coach..." />
          <button type="submit" className="round primary"><Send size={20} /></button>
        </form>
      )}

      <nav className="tabbar">
        {TABS.map(({ id, label, Icon }) => (
          <button key={id} className={"tab" + (tab === id ? " active" : "")} onClick={() => setTab(id)}>
            <Icon size={22} />
            {label}
          </button>
        ))}
      </nav>

      {menuOpen && (
        <>
          <div className="backdrop" onClick={() => setMenuOpen(false)} />
          <div className="sheet">
            <QuestionCards onPick={pick} />
          </div>
        </>
      )}
    </div>
  );
}