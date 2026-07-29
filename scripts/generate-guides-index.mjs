import { mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const guidesSourceDir = path.join(repoRoot, 'frontend', 'src', 'content', 'guides');
const guidesOutputDir = path.join(repoRoot, 'frontend', 'public', 'guides');
const guidesOutputPath = path.join(guidesOutputDir, 'index.json');

function parseFrontMatter(markdown) {
  const match = markdown.match(/^---\s*\r?\n([\s\S]*?)\r?\n---\s*\r?\n?/);
  if (!match) {
    throw new Error('Guide markdown file is missing valid front matter.');
  }

  const map = new Map();
  for (const line of match[1].split(/\r?\n/)) {
    const separatorIndex = line.indexOf(':');
    if (separatorIndex < 0) {
      continue;
    }

    const key = line.slice(0, separatorIndex).trim();
    const value = line.slice(separatorIndex + 1).trim().replace(/^"|"$/g, '');
    map.set(key, value);
  }

  const slug = map.get('slug') ?? '';
  if (!slug) {
    throw new Error('Guide markdown file front matter is missing slug.');
  }

  const published = map.get('published') ?? '';
  return { slug, published };
}

async function generateGuidesIndex() {
  const files = await readdir(guidesSourceDir);
  const markdownFiles = files
    .filter(file => file.toLowerCase().endsWith('.md'))
    .sort((left, right) => left.localeCompare(right, 'sv-SE'));

  const entries = [];
  for (const file of markdownFiles) {
    const fullPath = path.join(guidesSourceDir, file);
    const raw = await readFile(fullPath, 'utf8');
    entries.push(parseFrontMatter(raw));
  }

  const payload = {
    generatedAt: new Date().toISOString(),
    entries,
  };

  await mkdir(guidesOutputDir, { recursive: true });

  const json = `${JSON.stringify(payload, null, 2)}\n`.replace(/\n/g, '\r\n');
  await writeFile(guidesOutputPath, json, 'utf8');

  // eslint-disable-next-line no-console
  console.log(`Generated ${guidesOutputPath} with ${entries.length} guide entries.`);
}

generateGuidesIndex().catch(error => {
  // eslint-disable-next-line no-console
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
