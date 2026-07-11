import { fetchWithTimeout } from '../api/http';

interface LexiconFrontMatter {
  title: string;
  description: string;
  slug: string;
  keywords: string;
  category: string;
  author: string;
  published: string;
}

export interface LexiconArticle {
  frontMatter: LexiconFrontMatter;
  body: string;
}

export interface LexiconSummary {
  title: string;
  description: string;
  slug: string;
  category: string;
  author: string;
  published: string;
}

interface TailIndexRow {
  word: string;
  slug: string;
  title: string;
  description: string;
  isCore: boolean;
  shard: string | null;
}

interface TailEntryPayload {
  word: string;
  slug: string;
  title: string;
  description: string;
  keywords: string;
  category: string;
  author: string;
  published: string;
  definition: string;
  clues: string[];
  alternativeMeanings: string[];
  relatedWords: string[];
  difficulty: string;
  sources: string[];
  seoTitle: string;
  metaDescription: string;
}

interface TailShardPayload {
  shard: string;
  entries: TailEntryPayload[];
}

const CORE_FILES = import.meta.glob('./lexicon-core/*.md', {
  eager: true,
  query: '?raw',
  import: 'default',
}) as Record<string, string>;

const BASE_URL = import.meta.env.BASE_URL ?? '/';
const LEXICON_INDEX_TIMEOUT_MS = 10_000;
const LEXICON_SHARD_TIMEOUT_MS = 12_000;

export interface LexiconTelemetryEvent {
  type: 'index-load' | 'shard-load' | 'article-load' | 'prefetch';
  slug?: string;
  shard?: string;
  durationMs: number;
  success: boolean;
  fromCache?: boolean;
  statusCode?: number;
}

let telemetryReporter: ((event: LexiconTelemetryEvent) => void) | null = null;

export function setLexiconTelemetryReporter(reporter: ((event: LexiconTelemetryEvent) => void) | null): void {
  telemetryReporter = reporter;
}

function emitTelemetry(event: LexiconTelemetryEvent): void {
  try {
    telemetryReporter?.(event);
  } catch {
    // swallow telemetry errors
  }
}

function parseFrontMatter(raw: string): LexiconArticle {
  const frontMatterMatch = raw.match(/^---\s*\r?\n([\s\S]*?)\r?\n---\s*\r?\n?/);

  if (!frontMatterMatch) {
    throw new Error('Lexikonfil saknar giltig front matter.');
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

  const frontMatter: LexiconFrontMatter = {
    title: map.title ?? '',
    description: map.description ?? '',
    slug: map.slug ?? '',
    keywords: map.keywords ?? '',
    category: map.category ?? '',
    author: map.author ?? '',
    published: map.published ?? '',
  };

  if (!frontMatter.slug || !frontMatter.title) {
    throw new Error('Lexikonfil saknar obligatoriska fält i front matter.');
  }

  return { frontMatter, body };
}

const CORE_ARTICLES: LexiconArticle[] = Object.values(CORE_FILES)
  .map(parseFrontMatter)
  .sort((a, b) => a.frontMatter.title.localeCompare(b.frontMatter.title, 'sv-SE'));

const CORE_BY_SLUG = new Map(CORE_ARTICLES.map(article => [article.frontMatter.slug, article]));

let tailIndexPromise: Promise<TailIndexRow[]> | null = null;
const tailShardCache = new Map<string, Promise<Map<string, LexiconArticle>>>();

function buildMarkdownFromTailEntry(entry: TailEntryPayload): string {
  const clues = entry.clues.length > 0
    ? entry.clues.map(clue => `- ${clue}`).join('\n')
    : '- Inga etablerade ledtrådar hittades i källfilerna.';

  const alternative = entry.alternativeMeanings.length > 0
    ? entry.alternativeMeanings.map(value => `- ${value}`).join('\n')
    : '- Ordet används främst i en etablerad korsordsbetydelse i källmaterialet.';

  const examples = (entry.clues.length > 0 ? entry.clues.slice(0, 4) : [entry.definition])
    .map(clue => `- Ledtråd: **${clue}**  \n  Svar: **${entry.word}**`)
    .join('\n');

  const relatedWords = entry.relatedWords.map(word => `- ${word}`).join('\n');

  return `# ${entry.word}\n\n## Definition\n${entry.definition}\n\n## Common crossword clues\n${clues}\n\n## Alternative meanings\n${alternative}\n\n## Example clues and answers\n${examples}\n\n## Related crossword words\n${relatedWords}\n\n## FAQ\n### Vad betyder ${entry.word} i korsord?\n${entry.word} används oftast i betydelsen: ${entry.definition}\n\n### Vilka ledtrådar är vanligast för ${entry.word}?\nDe vanligaste i våra källor är sådana som liknar listan i avsnittet ‘Common crossword clues’.\n\n### Är ${entry.word} ett svårt korsordsord?\nKällmaterialet klassar ordet främst som **${entry.difficulty}**.\n\n### Varifrån kommer definitionerna för ${entry.word}?\nDefinitioner och ledtrådar är sammanställda från: ${entry.sources.join(', ')}.\n\n## SEO title\n${entry.seoTitle}\n\n## Meta description\n${entry.metaDescription}`;
}

async function loadTailIndex(): Promise<TailIndexRow[]> {
  if (!tailIndexPromise) {
    const startedAt = performance.now();

    tailIndexPromise = fetchWithTimeout(`${BASE_URL}lexicon-data/index.json`, {
      timeoutMs: LEXICON_INDEX_TIMEOUT_MS,
      retries: 1,
    })
      .then(async response => {
        const durationMs = Math.round(performance.now() - startedAt);

        if (!response.ok) {
          emitTelemetry({
            type: 'index-load',
            durationMs,
            success: false,
            statusCode: response.status,
          });
          throw new Error(`Kunde inte läsa lexikonindex (${response.status}).`);
        }

        const json = await response.json() as { entries?: TailIndexRow[] };
        emitTelemetry({
          type: 'index-load',
          durationMs,
          success: true,
          statusCode: response.status,
        });
        return Array.isArray(json.entries) ? json.entries : [];
      })
      .catch(() => {
        emitTelemetry({
          type: 'index-load',
          durationMs: Math.round(performance.now() - startedAt),
          success: false,
        });
        return [];
      });
  }

  return tailIndexPromise;
}

async function loadTailShard(shard: string): Promise<Map<string, LexiconArticle>> {
  const fromCache = tailShardCache.has(shard);

  if (!fromCache) {
    const startedAt = performance.now();

    const promise = fetchWithTimeout(`${BASE_URL}lexicon-data/shards/${shard}.json`, {
      timeoutMs: LEXICON_SHARD_TIMEOUT_MS,
      retries: 1,
    })
      .then(async response => {
        const durationMs = Math.round(performance.now() - startedAt);

        if (!response.ok) {
          emitTelemetry({
            type: 'shard-load',
            shard,
            durationMs,
            success: false,
            statusCode: response.status,
            fromCache: false,
          });
          throw new Error(`Kunde inte läsa lexikonshard ${shard} (${response.status}).`);
        }

        const payload = await response.json() as TailShardPayload;
        const map = new Map<string, LexiconArticle>();

        for (const entry of payload.entries ?? []) {
          map.set(entry.slug, {
            frontMatter: {
              title: entry.title,
              description: entry.description,
              slug: entry.slug,
              keywords: entry.keywords,
              category: entry.category,
              author: entry.author,
              published: entry.published,
            },
            body: buildMarkdownFromTailEntry(entry),
          });
        }

        emitTelemetry({
          type: 'shard-load',
          shard,
          durationMs,
          success: true,
          statusCode: response.status,
          fromCache: false,
        });

        return map;
      })
      .catch(() => {
        emitTelemetry({
          type: 'shard-load',
          shard,
          durationMs: Math.round(performance.now() - startedAt),
          success: false,
          fromCache: false,
        });
        return new Map<string, LexiconArticle>();
      });

    tailShardCache.set(shard, promise);
  }

  return tailShardCache.get(shard)!;
}

export async function getLexiconSummaries(): Promise<LexiconSummary[]> {
  const indexRows = await loadTailIndex();

  if (indexRows.length === 0) {
    return CORE_ARTICLES.map(({ frontMatter }) => ({
      title: frontMatter.title,
      description: frontMatter.description,
      slug: frontMatter.slug,
      category: frontMatter.category,
      author: frontMatter.author,
      published: frontMatter.published,
    }));
  }

  const summaries = new Map<string, LexiconSummary>();

  for (const row of indexRows) {
    const core = CORE_BY_SLUG.get(row.slug);
    if (core) {
      summaries.set(row.slug, {
        title: core.frontMatter.title,
        description: core.frontMatter.description,
        slug: core.frontMatter.slug,
        category: core.frontMatter.category,
        author: core.frontMatter.author,
        published: core.frontMatter.published,
      });
      continue;
    }

    summaries.set(row.slug, {
      title: row.title,
      description: row.description,
      slug: row.slug,
      category: 'Lexikon',
      author: 'SvensktKorsord.se',
      published: '',
    });
  }

  return Array.from(summaries.values()).sort((a, b) => a.title.localeCompare(b.title, 'sv-SE'));
}

const prefetchedSlugs = new Set<string>();

export async function prefetchLexiconBySlug(slug: string): Promise<void> {
  if (!slug || CORE_BY_SLUG.has(slug) || prefetchedSlugs.has(slug)) {
    return;
  }

  const startedAt = performance.now();
  prefetchedSlugs.add(slug);

  const indexRows = await loadTailIndex();
  const row = indexRows.find(item => item.slug === slug);
  if (!row || row.isCore || !row.shard) {
    emitTelemetry({
      type: 'prefetch',
      slug,
      durationMs: Math.round(performance.now() - startedAt),
      success: false,
    });
    return;
  }

  await loadTailShard(row.shard);
  emitTelemetry({
    type: 'prefetch',
    slug,
    shard: row.shard,
    durationMs: Math.round(performance.now() - startedAt),
    success: true,
  });
}

export async function prefetchLexiconBySlugs(slugs: string[], maxCount = 8): Promise<void> {
  const uniqueSlugs = Array.from(new Set(slugs.filter(Boolean))).slice(0, maxCount);

  await Promise.all(uniqueSlugs.map(slug => prefetchLexiconBySlug(slug)));
}

export async function getLexiconBySlug(slug: string): Promise<LexiconArticle | null> {
  const startedAt = performance.now();
  const core = CORE_BY_SLUG.get(slug);
  if (core) {
    emitTelemetry({
      type: 'article-load',
      slug,
      durationMs: Math.round(performance.now() - startedAt),
      success: true,
      fromCache: true,
    });
    return core;
  }

  const indexRows = await loadTailIndex();
  const row = indexRows.find(item => item.slug === slug);
  if (!row || row.isCore || !row.shard) {
    emitTelemetry({
      type: 'article-load',
      slug,
      durationMs: Math.round(performance.now() - startedAt),
      success: false,
    });
    return null;
  }

  const shardMap = await loadTailShard(row.shard);
  const article = shardMap.get(slug) ?? null;

  emitTelemetry({
    type: 'article-load',
    slug,
    shard: row.shard,
    durationMs: Math.round(performance.now() - startedAt),
    success: article !== null,
    fromCache: true,
  });

  return article;
}
