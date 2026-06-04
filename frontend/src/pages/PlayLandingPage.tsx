import { Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import type { PuzzleSize } from './puzzle/types';
import '../styles/static-pages.css';

const SIZE_OPTIONS: Array<{ key: PuzzleSize; label: string; description: string }> = [
  { key: '10x10', label: '🟢 Liten (10×10)', description: 'Snabb omgång med färre ord.' },
  { key: '15x15', label: '🟡 Mellan (15×15)', description: 'Balanserad storlek för de flesta spelare.' },
  { key: '17x17', label: '🔴 Stor (17×17)', description: 'Större utmaning med fler ord.' },
];

export default function PlayLandingPage() {
  usePageTitle('Välj storlek');
  
  useSEO({
    title: 'Välj storlek',
    description: 'Välj mellan tre svårighetsgrader: Liten (10×10), Mellan (15×15) eller Stor (17×17). Spela gratis svenska korsord online idag!',
    canonical: 'https://www.svensktkorsord.se/play',
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Spela', url: 'https://www.svensktkorsord.se/play' }
    ])
  });

  return (
    <>
      <h1>Välj storlek</h1>
      <p className="tagline">Starta spelet i den storlek som passar dig bäst.</p>

      <div className="landing-hero">
        {SIZE_OPTIONS.map(option => (
          <div key={option.key} className="hero-card">
            <h2>{option.label}</h2>
            <p>{option.description}</p>
            <Link to={`/puzzle?size=${option.key}`} className="hero-cta">
              Spela {option.key}
            </Link>
          </div>
        ))}
      </div>

      <div className="page-intro">
        <section>
          <h2>Vilken storlek ska jag välja?</h2>
          <p>
            Alla tre storlekar använder svenska ord och genereras automatiskt varje dag.
            Om du är ny på sajten rekommenderar vi att börja med <strong>10×10</strong> —
            det tar vanligtvis 3–8 minuter och är ett bra sätt att lära sig hur spelet fungerar.
          </p>
          <ul>
            <li><strong>10×10 — Liten:</strong> Perfekt för en snabb paus. Färre ord och kortare ledtrådar.</li>
            <li><strong>15×15 — Mellan:</strong> Den populäraste storleken. Lagom utmaning för de flesta spelare.</li>
            <li><strong>17×17 — Stor:</strong> För den erfarne. Fler korsningar och mer komplexa ledtrådar.</li>
          </ul>
        </section>
        <section>
          <h2>Så här spelar du</h2>
          <ul>
            <li>Klicka eller tryck på en ruta i korsordet för att markera den.</li>
            <li>Skriv en bokstav — markören hoppar automatiskt till nästa ruta i samma ord.</li>
            <li>Tryck <strong>Mellanslag</strong> för att byta riktning (vågrätt ↔ lodrätt).</li>
            <li>Tryck <strong>Tab</strong> för att hoppa till nästa ledtråd.</li>
            <li>När du löst korsordet kan du frivilligt ange ett alias och hamna på topplistan.</li>
          </ul>
        </section>
      </div>
    </>
  );
}
