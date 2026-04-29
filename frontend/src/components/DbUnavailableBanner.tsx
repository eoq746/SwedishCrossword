import { useState } from 'react';

export default function DbUnavailableBanner() {
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        zIndex: 9999,
        background: '#fff3cd',
        color: '#664d03',
        borderBottom: '1px solid #ffe69c',
        padding: '0.6rem 1rem',
        font: '14px/1.4 system-ui,sans-serif',
        display: 'flex',
        gap: '1rem',
        alignItems: 'center',
        justifyContent: 'center',
        boxShadow: '0 1px 4px rgba(0,0,0,.08)',
      }}
    >
      <span>
        ⚠️ Resultatlistor och statistik är tillfälligt otillgängliga – pussel fungerar som vanligt.
      </span>
      <button
        type="button"
        aria-label="Stäng"
        onClick={() => setDismissed(true)}
        style={{
          background: 'transparent',
          border: 0,
          color: '#664d03',
          fontSize: '1.1rem',
          cursor: 'pointer',
          padding: '0 .25rem',
          lineHeight: 1,
        }}
      >
        ×
      </button>
    </div>
  );
}
