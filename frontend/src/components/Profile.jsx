import { ChevronLeft } from "lucide-react";
import Avatar from "./Avatar";
import { TONES } from "./Setup";

export default function Profile({ coach, stats, onTone, onBack, onEdit }) {
  const items = [
    { label: "Questions answered", value: stats.questions },
    { label: "Leaks caught", value: "142 €" },
    { label: "Days under the limit", value: 6 },
    { label: "Saved with your coach", value: "85 €" },
  ];
  return (
    <div className="phone">
      <button className="back" onClick={onBack}><ChevronLeft size={20} /> Back</button>

      <div className="profile-top">
        <Avatar photo={coach.photo} name={coach.name} size={96} />
        <h1 className="coach-big">{coach.name}</h1>
        <div className="muted">Your personal economic trainer</div>
      </div>

      <label className="field-label">Tone</label>
      <div className="tones">
        {TONES.map((t) => (
          <button
            key={t.id}
            className={"tone" + (coach.tone === t.id ? " active" : "")}
            onClick={() => onTone(t.id)}
          >
            <b>{t.label}</b>
          </button>
        ))}
      </div>

      <div className="stats">
        {items.map((s) => (
          <div className="stat" key={s.label}>
            <div className="stat-value">{s.value}</div>
            <div className="muted">{s.label}</div>
          </div>
        ))}
      </div>

      <button className="primary-btn" onClick={onEdit}>Edit coach</button>
    </div>
  );
}