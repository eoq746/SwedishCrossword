import { Link, Navigate, useParams } from 'react-router-dom';
import { useEffect, useState } from 'react';
import MarkdownContent from '../components/MarkdownContent';
import { getLexiconBySlug, type LexiconArticle } from '../content/lexicon';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

export default function LexiconEntryPage() {
  const { slug = '' } = useParams();
  const [entry, setEntry] = useState<LexiconArticle | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);

    getLexiconBySlug(slug)
      .then(result => {
        if (!active) return;
        setEntry(result);
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [slug]);

  usePageTitle(entry?.frontMatter.title ?? 'Korsordslexikon');
  useSEO(
    entry
      ? {
          title: entry.frontMatter.title,
          description: entry.frontMatter.description,
          canonical: `https://www.svensktkorsord.se/lexikon/${entry.frontMatter.slug}`,
          ogType: 'article',
          keywords: entry.frontMatter.keywords,
          structuredData: generateBreadcrumbSchema([
            { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
            { name: 'Lexikon', url: 'https://www.svensktkorsord.se/lexikon' },
            { name: entry.frontMatter.title, url: `https://www.svensktkorsord.se/lexikon/${entry.frontMatter.slug}` },
          ]),
        }
      : undefined,
  );

  if (loading) {
    return <p className="lexicon-status">Laddar ordpost…</p>;
  }

  if (!entry) {
    return <Navigate to="/lexicon" replace />;
  }

  return (
    <>
      <p className="last-updated">Publicerad: {entry.frontMatter.published} · Av {entry.frontMatter.author}</p>
      <MarkdownContent markdown={entry.body} />
      <Link to="/lexicon" className="back-link">← Till lexikon</Link>
    </>
  );
}
