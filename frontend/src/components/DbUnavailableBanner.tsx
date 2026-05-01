import { useState } from 'react';

export default function DbUnavailableBanner() {
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      className="db-unavailable-banner"
    >
      <span>
        ⚠️ Resultatlistor och statistik är tillfälligt otillgängliga – pussel fungerar som vanligt.
      </span>
      <button
        type="button"
        aria-label="Stäng"
        onClick={() => setDismissed(true)}
        className="db-unavailable-banner-close"
      >
        ×
      </button>
    </div>
  );
}
