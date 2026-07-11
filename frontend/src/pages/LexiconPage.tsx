import { Link } from 'react-router-dom';
import { useEffect, useMemo, useState } from 'react';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { getLexiconSummaries, prefetchLexiconBySlug, prefetchLexiconBySlugs, type LexiconSummary } from '../content/lexicon';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

export default function LexiconPage() {
  const [query, setQuery] = useState('');
  const [entries, setEntries] = useState<LexiconSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    getLexiconSummaries()
      .then(result => {
        if (!active) return;
        setEntries(result);
        setLoadError(null);
      })
      .catch(() => {
        if (!active) return;
        setEntries([]);
        setLoadError('Kunde inte läsa lexikondata just nu.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return entries;

    return entries.filter(entry =>
      entry.title.toLowerCase().includes(q) ||
      entry.description.toLowerCase().includes(q),
    );
  }, [entries, query]);

  useEffect(() => {
    if (filtered.length === 0) {
      return;
    }

    const handle = window.setTimeout(() => {
      void prefetchLexiconBySlugs(filtered.slice(0, 8).map(entry => entry.slug));
    }, 180);

    return () => {
      window.clearTimeout(handle);
    };
  }, [filtered]);

  const handleLinkPrefetch = (slug: string) => {
    void prefetchLexiconBySlug(slug);
  };

  usePageTitle('Korsordslexikon');
  useSEO({
    title: 'Korsordslexikon',
    description: 'Sök bland korsordsord med definitioner, vanliga ledtrådar och exempel direkt från Svenskt Korsords ordkällor.',
    canonical: 'https://www.svensktkorsord.se/lexikon',
    ogType: 'website',
    keywords: 'korsordslexikon, korsordsord, svenska korsord, ledtrådar, ordlista',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Lexikon', url: 'https://www.svensktkorsord.se/lexikon' },
    ]),
  });

  return (
    <>
      <h1>Korsordslexikon</h1>
      <p className="tagline">Massuppdaterat från ordlistor – sök ord, betydelser och vanliga korsordsledtrådar.</p>

      <div className="lexicon-toolbar">
        <label htmlFor="lexicon-search" className="lexicon-search-label">Sök ord i lexikon</label>
        <input
          id="lexicon-search"
          className="lexicon-search"
          type="search"
          value={query}
          onChange={e => setQuery(e.target.value)}
          placeholder="Exempel: ost, norr, ara..."
        />
        <p className="lexicon-count">Visar {filtered.length} av {entries.length} ord</p>
      </div>

      {loading && <p className="lexicon-status">Laddar lexikon…</p>}
      {loadError && <p className="lexicon-status lexicon-status-error">{loadError}</p>}

      <div className="lexicon-grid">
        {!loading && !loadError && filtered.map(entry => (
          <article key={entry.slug} className="lexicon-card">
            <h2>{entry.title.replace(' – korsordslexikon', '')}</h2>
            <p>{entry.description}</p>
            <Link
              to={`/lexicon/${entry.slug}`}
              className="hero-cta"
              onMouseEnter={() => handleLinkPrefetch(entry.slug)}
              onFocus={() => handleLinkPrefetch(entry.slug)}
            >
              Öppna ord
            </Link>
          </article>
        ))}
      </div>

      <Link to="/" className="back-link">← Startsida</Link>
    </>
  );
}
