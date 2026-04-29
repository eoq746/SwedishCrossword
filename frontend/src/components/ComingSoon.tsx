import { usePageTitle } from '../hooks/usePageTitle';

interface ComingSoonProps {
  /** Swedish page title shown in heading and <title> */
  title: string;
  /** Path to the current legacy HTML page for the fallback link */
  legacyPath: string;
}

/** Placeholder rendered for routes not yet migrated to React. */
export default function ComingSoon({ title, legacyPath }: ComingSoonProps) {
  usePageTitle(title);

  return (
    <div style={{ padding: '2rem', textAlign: 'center' }}>
      <h2>{title}</h2>
      <p style={{ marginTop: '1rem' }}>
        <a href={legacyPath} className="back-link">
          Gå till nuvarande sida →
        </a>
      </p>
    </div>
  );
}
