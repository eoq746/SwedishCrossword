import { Link, Navigate, useParams } from 'react-router-dom';
import MarkdownContent from '../components/MarkdownContent';
import { getGuideBySlug } from '../content/guides';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

export default function GuideArticlePage() {
  const { slug = '' } = useParams();
  const guide = getGuideBySlug(slug);

  usePageTitle(guide?.frontMatter.title ?? 'Guider');
  useSEO(
    guide
      ? {
          title: guide.frontMatter.title,
          description: guide.frontMatter.description,
          canonical: `https://www.svensktkorsord.se/guides/${guide.frontMatter.slug}`,
          ogType: 'article',
          keywords: guide.frontMatter.keywords,
          structuredData: generateBreadcrumbSchema([
            { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
            { name: 'Guider', url: 'https://www.svensktkorsord.se/guides' },
            { name: guide.frontMatter.title, url: `https://www.svensktkorsord.se/guides/${guide.frontMatter.slug}` },
          ]),
        }
      : undefined,
  );

  if (!guide) {
    return <Navigate to="/guides" replace />;
  }

  return (
    <>
      <p className="last-updated">Publicerad: {guide.frontMatter.published} · Av {guide.frontMatter.author}</p>
      <MarkdownContent markdown={guide.body} />
      <Link to="/guides" className="back-link">← Till guider</Link>
    </>
  );
}
