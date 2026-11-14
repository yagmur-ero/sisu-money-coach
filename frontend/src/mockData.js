// Same shape the backend will send: { type, answer, data }
export const MOCK = {
  1: {
    type: "baskets",
    data: {
      income: 2400,
      spent: 1980,
      runOutLabel: "19th",
      baskets: [
        {
          key: "regular", label: "Mandatory, regular", amount: 1120, limit: 1200,
          items: [
            { name: "Rent", amount: 760 },
            { name: "Utilities and internet", amount: 120 },
            { name: "Insurance", amount: 90 },
            { name: "Phone", amount: 90 },
            { name: "Transit pass", amount: 60 },
          ],
        },
        {
          key: "irregular", label: "Mandatory, irregular", amount: 380, limit: 300,
          items: [
            { name: "Washing machine repair", amount: 210 },
            { name: "Dentist", amount: 110 },
            { name: "Bike service", amount: 60 },
          ],
        },
        {
          key: "leaks", label: "Everyday leaks", amount: 480, limit: 250,
          items: [
            { name: "Food delivery", amount: 168 },
            { name: "Coffee and cafes", amount: 142 },
            { name: "Kiosk and snacks", amount: 96 },
            { name: "Impulse online shopping", amount: 74 },
          ],
        },
      ],
    },
    answers: {
      gentle: "Most of your money went to rent and bills, which is normal. Your everyday spending was 480 EUR, a bit above the comfortable 250 EUR.",
      honest: "Rent and bills took the biggest part. Everyday spending hit 480 EUR, almost double the safe limit.",
      roast: "Rent is fine, rent is rent. But 480 EUR on everyday stuff? Your coffee habit has its own tank now.",
    },
  },
  2: {
    type: "comparison",
    data: {
      this_month: 1980,
      last_month: 1770,
      items: [
        { name: "Restaurants", this: 260, last: 150 },
        { name: "Entertainment", this: 120, last: 50 },
        { name: "Shopping", this: 180, last: 140 },
        { name: "Groceries", this: 420, last: 425 },
        { name: "Transport", this: 90, last: 95 },
      ],
      unusual: { label: "Concert tickets", amount: 70 },
    },
    answers: {
      gentle: "You spent about 210 EUR more than last month. Restaurants and concert tickets explain most of it.",
      honest: "Spending is up 12%. Restaurants (+110 EUR) and entertainment (+70 EUR) are the cause.",
      roast: "Up 12%. Your restaurant budget is apparently a hobby now.",
    },
  },
  3: {
    type: "leaks",
    data: {
      total: 480,
      items: [
        { name: "Food delivery", count: 11, amount: 168, save: 70 },
        { name: "Coffee and cafes", count: 34, amount: 142, save: 60 },
        { name: "Kiosk and snacks", count: 27, amount: 96, save: 40 },
        { name: "Impulse online shopping", count: 6, amount: 74, save: 30 },
      ],
    },
    answers: {
      gentle: "Small purchases add up. Around 200 EUR of them would be easy to save.",
      honest: "34 coffees, 11 deliveries, 27 kiosk stops. You could keep about 200 EUR a month.",
      roast: "34 coffees. Your barista knows your name, your bank doesn't know where the money went. Buy that kiosk snack again and you'll be hungry on the 19th.",
    },
  },
  4: {
    type: "safe_to_spend",
    data: {
      balance: 600,
      bills: 310,
      savings: 100,
      buffer: 50,
      events: [{ name: "Friend's gift", date: "Fri", amount: 40 }],
      days_left: 12,
    },
    answers: {
      gentle: "After bills, savings and your buffer you can safely spend about 100 EUR until payday. Friday's gift is already counted.",
      honest: "Safe to spend: 100 EUR over 12 days, about 8 EUR a day.",
      roast: "8 EUR a day. That is one coffee and a stern look.",
    },
  },
  5: {
    type: "what_if",
    data: { restaurants: 260, shopping: 180 },
    answers: {
      gentle: "Try a few small changes. Move the sliders and see what they add up to.",
      honest: "Pick your changes. The numbers below show what they are worth.",
      roast: "Go on, be brave. Cut something. The sliders won't judge you, I will.",
    },
  },
};

// Overview shown on the main page (same shape as GET /home)
export const HOME = {
  balance: 600,
  accounts: [
    { id: "a1", name: "Current account", balance: 480 },
    { id: "a2", name: "Savings account", balance: 120 },
  ],
  recent: [
    { id: 1, date: "2026-11-14", title: "Coffee shop", category: "Coffee and cafes", amount: -4.8 },
    { id: 2, date: "2026-11-13", title: "Food delivery", category: "Food delivery", amount: -18.9 },
    { id: 3, date: "2026-11-13", title: "Grocery store", category: "Groceries", amount: -32.45 },
    { id: 4, date: "2026-11-12", title: "Kiosk", category: "Kiosk and snacks", amount: -6.5 },
    { id: 5, date: "2026-11-11", title: "Tax refund", category: "Income", amount: 85 },
  ],
  month: { income: 2400, expenses: 1980 },
  budget: { monthly: 2200, safe_to_spend: 100, days_left: 12 },
};