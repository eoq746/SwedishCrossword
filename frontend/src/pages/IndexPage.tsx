import { Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

const CARDS = [
  {
    emoji: '🧩',
    title: 'Spela',
    desc: 'Lös dagens korsord och testa ditt ordförråd.',
    cta: 'Spela nu',
    to: '/play',
  },
  {
    emoji: '🏆',
    title: 'Topplista',
    desc: 'Se hur du placerar dig mot andra spelare idag.',
    cta: 'Se topplistan',
    to: '/leaderboard',
  },
  {
    emoji: '📅',
    title: 'Arkiv',
    desc: 'Spela tidigare korsord från vårt arkiv.',
    cta: 'Bläddra i arkivet',
    to: '/calendar',
  },
] as const;

export default function IndexPage() {
  usePageTitle();
  
  useSEO({
    description: 'Spela gratis svenska korsord online varje dag. Nytt korsord dagligen med svenska ord, synonymer och ledtrådar. Tävla på topplistan, träna hjärnan och förbättra ditt ordförråd!',
    canonical: 'https://www.svensktkorsord.se/',
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' }
    ])
  });

  return (
    <>
      <h1>Dagens Korsord</h1>
      <p className="tagline">
        Gratis dagliga korsord på svenska — nytt pussel varje dag.
      </p>

      <div className="landing-hero">
        {CARDS.map(card => (
          <div key={card.to} className="hero-card">
            <span style={{ fontSize: '2.2rem', lineHeight: 1 }} aria-hidden="true">
              {card.emoji}
            </span>
            <h2>{card.title}</h2>
            <p>{card.desc}</p>
            <Link to={card.to} className="back-link">
              {card.cta}
            </Link>
          </div>
        ))}
      </div>
    </>
  );
}
