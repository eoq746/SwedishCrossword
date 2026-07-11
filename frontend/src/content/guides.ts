interface GuideFrontMatter {
  title: string;
  description: string;
  slug: string;
  keywords: string;
  category: string;
  author: string;
  published: string;
}

export interface GuideArticle {
  frontMatter: GuideFrontMatter;
  body: string;
}

export interface GuideSummary {
  title: string;
  description: string;
  slug: string;
  category: string;
  author: string;
  published: string;
}

const GUIDE_FILES = import.meta.glob('./guides/*.md', {
  eager: true,
  query: '?raw',
  import: 'default',
}) as Record<string, string>;

function parseFrontMatter(raw: string): GuideArticle {
  const frontMatterMatch = raw.match(/^---\s*\r?\n([\s\S]*?)\r?\n---\s*\r?\n?/);

  if (!frontMatterMatch) {
    throw new Error('Markdownfil saknar giltig front matter.');
  }

  const frontMatterBlock = frontMatterMatch[1];
  const body = raw.slice(frontMatterMatch[0].length).trim();
  const entries = frontMatterBlock
    .split(/\r?\n/)
    .filter(line => line.includes(':'))
    .map(line => {
      const separatorIndex = line.indexOf(':');
      const key = line.slice(0, separatorIndex).trim();
      const value = line.slice(separatorIndex + 1).trim().replace(/^"|"$/g, '');
      return [key, value] as const;
    });

  const map = Object.fromEntries(entries);

  const frontMatter: GuideFrontMatter = {
    title: map.title ?? '',
    description: map.description ?? '',
    slug: map.slug ?? '',
    keywords: map.keywords ?? '',
    category: map.category ?? '',
    author: map.author ?? '',
    published: map.published ?? '',
  };

  if (!frontMatter.slug || !frontMatter.title) {
    throw new Error('Markdownfil saknar obligatoriska fält i front matter.');
  }

  return { frontMatter, body };
}

const GUIDES: GuideArticle[] = Object.values(GUIDE_FILES)
  .map(parseFrontMatter)
  .sort((a, b) => b.frontMatter.published.localeCompare(a.frontMatter.published));

export function getGuideSummaries(): GuideSummary[] {
  return GUIDES.map(({ frontMatter }) => ({
    title: frontMatter.title,
    description: frontMatter.description,
    slug: frontMatter.slug,
    category: frontMatter.category,
    author: frontMatter.author,
    published: frontMatter.published,
  }));
}

export function getGuideBySlug(slug: string): GuideArticle | null {
  return GUIDES.find(guide => guide.frontMatter.slug === slug) ?? null;
}
