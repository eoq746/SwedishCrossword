import { Fragment, type ReactNode } from 'react';

interface MarkdownContentProps {
  markdown: string;
}

function renderInline(text: string): ReactNode[] {
  const tokens = text.split(/(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^\)]+\))/g).filter(Boolean);

  return tokens.map((token, index) => {
    if (token.startsWith('**') && token.endsWith('**')) {
      return <strong key={`strong-${index}`}>{token.slice(2, -2)}</strong>;
    }

    if (token.startsWith('`') && token.endsWith('`')) {
      return <code key={`code-${index}`}>{token.slice(1, -1)}</code>;
    }

    const linkMatch = token.match(/^\[([^\]]+)\]\(([^\)]+)\)$/);
    if (linkMatch) {
      return (
        <a key={`link-${index}`} href={linkMatch[2]}>
          {linkMatch[1]}
        </a>
      );
    }

    return <Fragment key={`text-${index}`}>{token}</Fragment>;
  });
}

export default function MarkdownContent({ markdown }: MarkdownContentProps) {
  const lines = markdown.split(/\r?\n/);
  const elements: ReactNode[] = [];
  let i = 0;

  while (i < lines.length) {
    const rawLine = lines[i];
    const line = rawLine.trim();

    if (!line) {
      i += 1;
      continue;
    }

    if (line.startsWith('# ')) {
      elements.push(<h1 key={`h1-${i}`}>{renderInline(line.slice(2))}</h1>);
      i += 1;
      continue;
    }

    if (line.startsWith('## ')) {
      elements.push(<h2 key={`h2-${i}`}>{renderInline(line.slice(3))}</h2>);
      i += 1;
      continue;
    }

    if (line.startsWith('### ')) {
      elements.push(<h3 key={`h3-${i}`}>{renderInline(line.slice(4))}</h3>);
      i += 1;
      continue;
    }

    if (/^\d+\.\s+/.test(line)) {
      const listItems: ReactNode[] = [];
      while (i < lines.length && /^\d+\.\s+/.test(lines[i].trim())) {
        const listLine = lines[i].trim().replace(/^\d+\.\s+/, '');
        listItems.push(<li key={`ol-${i}`}>{renderInline(listLine)}</li>);
        i += 1;
      }
      elements.push(<ol key={`olist-${i}`}>{listItems}</ol>);
      continue;
    }

    if (line.startsWith('- ')) {
      const listItems: ReactNode[] = [];
      while (i < lines.length && lines[i].trim().startsWith('- ')) {
        const listLine = lines[i].trim().slice(2);
        listItems.push(<li key={`ul-${i}`}>{renderInline(listLine)}</li>);
        i += 1;
      }
      elements.push(<ul key={`ulist-${i}`}>{listItems}</ul>);
      continue;
    }

    const paragraphLines: string[] = [line];
    i += 1;
    while (i < lines.length) {
      const next = lines[i].trim();
      if (!next || next.startsWith('#') || /^\d+\.\s+/.test(next) || next.startsWith('- ')) {
        break;
      }
      paragraphLines.push(next);
      i += 1;
    }

    elements.push(<p key={`p-${i}`}>{renderInline(paragraphLines.join(' '))}</p>);
  }

  return <article className="markdown-content">{elements}</article>;
}
