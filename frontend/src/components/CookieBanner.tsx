import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';

const CONSENT_KEY = 'cookie_consent';
const CONSENT_MAX_AGE_MS = 365 * 24 * 60 * 60 * 1000; // 1 year

type ConsentValue = 'all' | 'essential';

interface StoredConsent {
  value: ConsentValue;
  timestamp: number;
}

function getStoredConsent(): ConsentValue | null {
  try {
    const raw = localStorage.getItem(CONSENT_KEY);
    if (!raw) return null;
    // Support legacy plain-string values (treat as expired to re-prompt once)
    if (raw === 'all' || raw === 'essential') return null;
    const parsed: StoredConsent = JSON.parse(raw) as StoredConsent;
    if (Date.now() - parsed.timestamp > CONSENT_MAX_AGE_MS) return null;
    if (parsed.value === 'all' || parsed.value === 'essential') return parsed.value;
    return null;
  } catch {
    return null;
  }
}

function storeConsent(value: ConsentValue): void {
  try {
    const entry: StoredConsent = { value, timestamp: Date.now() };
    localStorage.setItem(CONSENT_KEY, JSON.stringify(entry));
  } catch {
    /* ignore */
  }
}

/** Apply Google Consent Mode v2 signals if gtag is available. */
function applyGtagConsent(value: ConsentValue): void {
  if (typeof window.gtag !== 'function') return;
  const granted = value === 'all' ? 'granted' : 'denied';
  window.gtag('consent', 'update', {
    ad_storage: granted,
    ad_user_data: granted,
    ad_personalization: granted,
    analytics_storage: granted,
  });
}

export default function CookieBanner() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (getStoredConsent() === null) {
      setVisible(true);
    }
  }, []);

  useEffect(() => {
    if (!visible) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') acceptEssential();
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible]);

  function accept() {
    storeConsent('all');
    applyGtagConsent('all');
    setVisible(false);
  }

  function acceptEssential() {
    storeConsent('essential');
    applyGtagConsent('essential');
    setVisible(false);
  }

  if (!visible) return null;

  return (
    <div
      id="cookie-consent-banner"
      role="dialog"
      aria-modal="true"
      aria-label="Cookie-samtycke"
      aria-live="polite"
    >
      <div className="cookie-consent-inner">
        <p>
          Vi använder cookies för webbplatsens funktion och för att visa relevanta annonser via
          Google AdSense. Nödvändiga cookies krävs för att sidan ska fungera. Läs mer i vår{' '}
          <Link to="/privacy-policy">integritetspolicy</Link>.
        </p>
        <div className="cookie-consent-buttons">
          <button className="cookie-btn cookie-btn-accept" onClick={accept}>
            Acceptera alla
          </button>
          <button className="cookie-btn cookie-btn-reject" onClick={acceptEssential}>
            Endast nödvändiga
          </button>
        </div>
      </div>
    </div>
  );
}
