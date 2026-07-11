import { Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { getGuideSummaries } from '../content/guides';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

export default function GuidesPage() {
  const guides = getGuideSummaries();

  usePageTitle('Guider');
  useSEO({
    title: 'Guider för korsordslösare',
    description: 'Läs guider med tips, tekniker och praktiska exempel för att bli bättre på svenska korsord.',
    canonical: 'https://www.svensktkorsord.se/guides',
    ogType: 'article',
    keywords: 'korsordsguider, svenska korsord, korsordstips, lär dig lösa korsord',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Guider', url: 'https://www.svensktkorsord.se/guides' },
    ]),
  });

  return (
    <>
      <h1>Guider för svenska korsord</h1>
      <p className="tagline">Fördjupade artiklar för bättre teknik, snabbare lösning och smartare ordstrategier.</p>

      <div className="guides-grid">
        {guides.map(guide => (
          <article key={guide.slug} className="guide-card">
            <p className="guide-meta">{guide.category} · {guide.published}</p>
            <h2>{guide.title}</h2>
            <p>{guide.description}</p>
            <Link to={`/guides/${guide.slug}`} className="hero-cta">Läs guide</Link>
          </article>
        ))}
      </div>

      <Link to="/" className="back-link">← Startsida</Link>
    </>
  );
}
