import { Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

export default function AboutPage() {
  usePageTitle('Om Oss - Gratis Korsord Online');
  
  useSEO({
    title: 'Om Oss',
    description: 'Läs om Svenskt Korsord - en plats för dagliga svenska korsord online. Lär dig hur vi skapar unika, utmanande pussel med en algoritm skriven i C# och .NET.',
    canonical: 'https://www.svensktkorsord.se/about',
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Om Oss', url: 'https://www.svensktkorsord.se/about' }
    ])
  });

  return (
    <>
      <h1>Om Svenskt Korsord</h1>
      <p className="tagline">Dagliga svenska korsord online</p>

      <p>Välkommen till Svenskt Korsord, din destination för dagliga <strong>korsord online</strong> på svenska. Vi erbjuder ett nytt, unikt <strong>gratis korsord</strong> varje dag som är speciellt designat för att utmana och underhålla svenska korsordsentusiaster i alla åldrar.</p>

      <h2>Vår vision och mission</h2>
      <p>Vi tror att <strong>korsord</strong> är mer än bara tidsfördriv – det är mental träning, ordkunskap och glädje i ett och samma paket. Vår mission är att göra högkvalitativa <strong>svenska korsord</strong> tillgängliga för alla, helt gratis.</p>
      <p>Du kan börja spela <strong>korsord online</strong> direkt utan att skapa något konto. Dina framsteg sparas lokalt i din webbläsare, och du kan välja att delta på topplistan helt anonymt. Vill du tävla med vänner eller spara din statistik över tid kan du logga in med Google eller Microsoft – helt frivilligt.</p>

      <h2>Hur vi skapar korsorden</h2>
      <p>Varje korsord på Svenskt Korsord genereras automatiskt med hjälp av en egenutvecklad algoritm skriven i C# och .NET. Processen är noggrant utformad för att säkerställa att varje pussel är både utmanande och rättvist.</p>

      <div className="tech-section">
        <h3>Teknisk process för korsordsgenereringen</h3>
        <ol>
          <li><strong>Ordval och filtrering:</strong> Algoritmen börjar med att välja kandidatord från vår omfattande ordlista med över 100&nbsp;000 svenska ord. Ord filtreras baserat på längd, svårighetsgrad och lämplighet för korsord.</li>
          <li><strong>Rutnätskonstruktion:</strong> Ett optimerat rutnät skapas där så många ord som möjligt korsar varandra. Algoritmen prioriterar korsningar som delar vanliga bokstäver som A, E, N, R och S för att maximera sammankopplingarna.</li>
          <li><strong>Intersektionsoptimering:</strong> För varje ord beräknas en poäng baserad på hur väl det kan korsas med andra ord. Ord med höga poäng placeras först för att skapa en stabil grund.</li>
          <li><strong>Ledtrådsgenering:</strong> Varje ord får en ledtråd baserad på dess betydelse, synonymer eller användning. Vi strävar efter att ledtrådarna ska vara informativa men inte för uppenbara.</li>
          <li><strong>Kvalitetskontroll:</strong> Innan publicering verifieras att alla ord finns i ordlistan, att det inte finns dubbletter, och att rutnätet har acceptabel täckningsgrad (minst 60% fyllda rutor).</li>
          <li><strong>Daglig publicering:</strong> Ett nytt korsord genereras och publiceras automatiskt varje dag vid midnatt UTC.</li>
        </ol>
      </div>

      <h2>Vår ordlista och dess källor</h2>
      <p>Kvaliteten på ett <strong>korsord</strong> står och faller med ordlistan. Vi har lagt ner betydande arbete på att sammanställa en omfattande och kvalitetsgranskad svensk ordlista som inkluderar:</p>
      <ul>
        <li><strong>Offentliga ordlistor:</strong> Grundstommen kommer från offentligt tillgängliga svenska lexikon och ordlistor som är licensierade för återanvändning.</li>
        <li><strong>LEXIN-baserade ord:</strong> En del av våra ord och definitioner härstammar från LEXIN, ett lexikon utvecklat för inlärning av svenska.</li>
        <li><strong>Synonymdatabaser:</strong> Vi har integrerat <strong>synonymer</strong> från Folkets synonymlexikon för att kunna erbjuda varierade ledtrådar och för att algoritmen ska kunna välja bland flera alternativa definitioner.</li>
        <li><strong>Kelly-listan:</strong> Vi använder den frekvensbaserade Kelly-ordlistan för att utöka vår ordvalidering. Källa: Kilgarriff, A., et al. (2014). <em>Language Resources and Evaluation</em>, 48:121–163, DOI{' '}
          <a href="https://doi.org/10.1007/s10579-013-9251-2" target="_blank" rel="noopener noreferrer">10.1007/s10579-013-9251-2</a>.
        </li>
        <li><strong>DSSO (Den Stora Svenska Ordlistan):</strong> En omfattande svensk ordlista från{' '}
          <a href="https://dsso.se/" target="_blank" rel="noopener noreferrer">dsso.se</a> (version 1.51). Licensierad under{' '}
          <a href="https://creativecommons.org/licenses/by-sa/3.0/" target="_blank" rel="noopener noreferrer">Creative Commons Erkännande-DelaLika 3.0</a>.
        </li>
        <li><strong>Egna tillägg:</strong> Vi har lagt till moderna ord, uttryck och begrepp som inte alltid finns i traditionella ordlistor men som är vanliga i vardagligt språkbruk.</li>
      </ul>

      <div className="license-box">
        <strong>Licensinformation:</strong> Delar av ordlistan är licensierade under{' '}
        <a href="https://creativecommons.org/licenses/by/2.5/se/" target="_blank" rel="noopener noreferrer">Creative Commons Erkännande 2.5 Sverige</a>. DSSO-ord är licensierade under{' '}
        <a href="https://creativecommons.org/licenses/by-sa/3.0/" target="_blank" rel="noopener noreferrer">Creative Commons Erkännande-DelaLika 3.0</a>. Vi är tacksamma för det arbete som gjorts för att tillgängliggöra svenska språkresurser.
      </div>

      <h2>Redaktionell process och kvalitetssäkring</h2>
      <ul>
        <li><strong>Ordvalidering:</strong> Varje ord i korsordet verifieras mot vår huvudordlista.</li>
        <li><strong>Olämpligt innehåll:</strong> Vi filtrerar bort potentiellt stötande, vulgära eller olämpliga ord.</li>
        <li><strong>Ledtrådskvalitet:</strong> Ledtrådarna granskas för att säkerställa att de är relevanta och inte vilseledande.</li>
        <li><strong>Rutnätsbedömning:</strong> Algoritmen utvärderar varje genererat rutnät och behåller endast de som uppfyller våra kvalitetskriterier.</li>
        <li><strong>Användarbidrag:</strong> Vi välkomnar feedback och granskar regelbundet rapporter om felaktiga ord eller ledtrådar.</li>
      </ul>

      <h2>Funktioner och egenskaper</h2>
      <div className="feature-grid">
        {[
          { icon: '📅', title: 'Dagliga pussel', text: 'Ett helt nytt korsord varje dag vid midnatt. Du behöver aldrig spela samma pussel två gånger.' },
          { icon: '🇸🇪', title: '100% svenska', text: 'Alla ord och ledtrådar är på svenska med fullt stöd för Å, Ä och Ö.' },
          { icon: '🏆', title: 'Topplista', text: 'Tävla mot andra spelare och se vem som löser dagens korsord snabbast. Helt anonymt och frivilligt.' },
          { icon: '📱', title: 'Fungerar överallt', text: 'Responsiv design som fungerar lika bra på mobil, surfplatta och dator.' },
          { icon: '💾', title: 'Autospara', text: 'Dina framsteg sparas automatiskt lokalt. Ta en paus och fortsätt senare utan att förlora något.' },
          { icon: '🔒', title: 'Integritetsfokus', text: 'Spela utan konto. Valfri inloggning via Google eller Microsoft för statistik och vänner.' },
        ].map(({ icon, title, text }) => (
          <div className="feature-card" key={title}>
            <div className="icon">{icon}</div>
            <h3>{title}</h3>
            <p>{text}</p>
          </div>
        ))}
      </div>

      <h2>Projektet bakom tjänsten</h2>
      <p>Svenskt Korsord är ett hobbyprojekt skapat av en liten grupp svenska utvecklare med passion för ordspel och programmering. Koden är delvis öppen källkod och finns tillgänglig på{' '}
        <a href="https://github.com/eoq746/SwedishCrossword" target="_blank" rel="noopener noreferrer">GitHub</a>. Vi välkomnar bidrag, buggrapporter och förslag på förbättringar.
      </p>

      <h2>Framtida planer</h2>
      <ul>
        <li>Temakorsord med specifika ämnen (sport, mat, geografi, etc.)</li>
        <li>Mobilapp (native)</li>
        <li>Miniligasystem för vängrupper</li>
      </ul>

      <h2>Kontakta oss</h2>
      <p>Besök vår <Link to="/contact">kontaktsida</Link>.</p>

      <Link to="/" className="back-link">← Startsida</Link>
    </>
  );
}
