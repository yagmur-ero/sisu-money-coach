import { useState, useEffect } from "react";
import { getBaskets } from "../api";
import { Baskets } from "./AnswerCard";
import SpenderType from "./SpenderType";

const HELP = [
  { title: "Mandatory, regular", text: "Rent, bills, subscriptions. About the same every month." },
  { title: "Mandatory, irregular", text: "Repairs, dentist, yearly fees. Not monthly, but they will come." },
  { title: "Everyday leaks", text: "Small purchases that slip through your fingers, like coffees and snacks." },
];

export default function BasketsPage({ tone }) {
  const [data, setData] = useState(undefined); // undefined = loading, null = failed
  const [tries, setTries] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setData(undefined);
    getBaskets()
      .then((d) => !cancelled && setData(d))
      .catch(() => !cancelled && setData(null));
    return () => {
      cancelled = true;
    };
  }, [tries]);

  return (
    <div>
      <h1 className="greeting compact">Your three baskets</h1>
      <div className="basket-help">
        {HELP.map((h) => (
          <div key={h.title}>
            <b>{h.title}</b>
            <div className="muted">{h.text}</div>
          </div>
        ))}
      </div>

      {data === undefined && <div className="home-card muted">Loading your baskets...</div>}
      {data === null && (
        <div className="home-card muted">
          Could not load your baskets.{" "}
          <button className="see-more" onClick={() => setTries((n) => n + 1)}>Try again</button>
        </div>
      )}
      {data && (
        <>
          <Baskets data={data} />
          <SpenderType data={data} tone={tone} />
        </>
      )}
    </div>
  );
}