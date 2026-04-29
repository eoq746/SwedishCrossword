import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';

const CONSENT_KEY = 'cookie_consent';

type ConsentValue = 'all' | 'essential';

function getStoredConsent(): ConsentValue | null {
  try {
    const val = localStorage.getItem(CONSENT_KEY);
    if (val === 'all' || val === 'essential') return val;
    return null;
  } catch {
    return null;
  }
}

function storeConsent(value: ConsentValue): void {
  try {
    localStorage.setItem(CONSENT_KEY, value);
  } catch {
    /* ignore */
  }
}

export default function CookieBanner() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (getStoredConsent() === null) {
      setVisible(true);
    }
  }, []);

  function accept() {
    storeConsent('all');
    setVisible(false);
  }

  function acceptEssential() {
    storeConsent('essential');
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
          Vi använder cookies för att förbättra din upplevelse. Nödvändiga cookies krävs för att
          sidan ska fungera. Läs mer i vår{' '}
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
