import { useState } from "react";
import { Camera } from "lucide-react";
import Avatar from "./Avatar";

export const TONES = [
  { id: "gentle", label: "Gentle", hint: "Kind and encouraging" },
  { id: "honest", label: "Honest", hint: "Straight to the point" },
  { id: "roast", label: "Roast", hint: "Funny and judgy about your numbers" },
];

// Crops the photo to a small square so it fits in browser storage
function resizeImage(file, size = 256) {
  return new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => {
      const img = new Image();
      img.onload = () => {
        const canvas = document.createElement("canvas");
        canvas.width = size;
        canvas.height = size;
        const s = Math.min(img.width, img.height);
        canvas
          .getContext("2d")
          .drawImage(img, (img.width - s) / 2, (img.height - s) / 2, s, s, 0, 0, size, size);
        resolve(canvas.toDataURL("image/jpeg", 0.85));
      };
      img.src = reader.result;
    };
    reader.readAsDataURL(file);
  });
}

export default function Setup({ initial, onSave, onCancel }) {
  const [name, setName] = useState(initial?.name ?? "");
  const [tone, setTone] = useState(initial?.tone ?? "honest");
  const [photo, setPhoto] = useState(initial?.photo ?? null);

  async function pickPhoto(e) {
    const file = e.target.files?.[0];
    if (file) setPhoto(await resizeImage(file));
  }

  return (
    <div className="phone setup">
      <h1 className="greeting">{initial ? "Edit your coach" : "Meet your coach"}</h1>
      <p className="muted">Make it yours. Your photo stays on this device.</p>

      <label className="photo-pick">
        <Avatar photo={photo} name={name || "Coach"} size={96} />
        <span className="photo-badge"><Camera size={16} /></span>
        <input type="file" accept="image/*" onChange={pickPhoto} hidden />
      </label>

      <label className="field-label">Coach name</label>
      <input
        className="text-field"
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="e.g. Aino"
      />

      <label className="field-label">Tone</label>
      <div className="tones">
        {TONES.map((t) => (
          <button
            key={t.id}
            type="button"
            className={"tone" + (tone === t.id ? " active" : "")}
            onClick={() => setTone(t.id)}
          >
            <b>{t.label}</b>
            <span>{t.hint}</span>
          </button>
        ))}
      </div>

      <button className="primary-btn" onClick={() => onSave({ name: name.trim() || "Coach", tone, photo })}>
        Start
      </button>
      {onCancel && (
        <button className="link-btn" onClick={onCancel}>Cancel</button>
      )}
    </div>
  );
}