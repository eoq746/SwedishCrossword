import { Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import '../styles/static-pages.css';

const FAQ_ITEMS: { q: string; a: React.ReactNode }[] = [
  {
    q: 'När uppdateras korsordet?',
    a: 'Ett nytt korsord genereras automatiskt varje dag vid midnatt UTC (01:00 svensk vintertid, 02:00 sommartid). Om du vill ha ett nytt pussel, kom tillbaka imorgon!',
  },
  {
    q: 'Kan jag spela gamla korsord?',
    a: <>Ja! Vi har ett <Link to="/calendar">arkiv</Link> med tidigare pussel. Där kan du bläddra per månad och storlek och spela korsord du missat.</>,
  },
  {
    q: 'Hur fungerar topplistan?',
    a: 'När du löser korsordet kan du frivilligt ange ett namn för att visas på topplistan. Topplistan visar de 10 snabbaste tiderna för dagens korsord och delas mellan alla spelare. Den nollställs automatiskt vid midnatt.',
  },
  {
    q: 'Varför markeras mitt resultat med en varning?',
    a: 'Vi har ett anti-fusk-system som upptäcker misstänkt aktivitet som extremt snabba tider eller användning av utvecklarverktyg i webbläsaren. Systemet är utformat för att säkerställa rättvis konkurrens på topplistan.',
  },
  {
    q: 'Fungerar sidan på mobilen?',
    a: 'Ja! Svenskt Korsord är responsivt designat och fungerar utmärkt på datorer, surfplattor och mobiltelefoner. Gränssnittet anpassar sig automatiskt efter skärmstorleken.',
  },
  {
    q: 'Hur sparas mina framsteg?',
    a: 'Dina framsteg sparas lokalt i din webbläsare med hjälp av localStorage. Dina ifyllda svar finns kvar även om du stänger webbläsaren. Observera att om du rensar webbläsarens data försvinner dina sparade framsteg.',
  },
  {
    q: 'Kan jag föreslå nya ord eller ledtrådar?',
    a: <>Absolut! Du kan skicka förslag via <a href="https://github.com/eoq746/SwedishCrossword/discussions" target="_blank" rel="noopener noreferrer">GitHub Discussions</a> eller skapa en issue. Inkludera gärna en källa eller definition för nya ord.</>,
  },
  {
    q: 'Är tjänsten gratis?',
    a: 'Ja, Svenskt Korsord är helt gratis att använda. Det krävs ingen prenumeration eller betalning för att spela.',
  },
];

export default function ContactPage() {
  usePageTitle('Kontakt');

  return (
    <>
      <h1>Kontakta Oss</h1>
      <p className="tagline">Vi vill gärna höra från dig!</p>

      <p>Har du frågor, förslag eller feedback om Svenskt Korsord? Vi uppskattar all kontakt och gör vårt bästa för att svara så snart som möjligt. Eftersom vi är ett litet team med begränsade resurser kan svarstiden variera, men vi läser alla meddelanden och tar dem på allvar.</p>

      <h2>Kontaktvägar</h2>
      <div className="contact-grid">
        <div className="contact-card">
          <div className="icon">🐛</div>
          <h3>Rapportera problem</h3>
          <p>Hittat en bugg, ett felaktigt ord eller en dålig ledtråd? Rapportera det så att vi kan förbättra tjänsten.</p>
          <p><a href="https://github.com/eoq746/SwedishCrossword/issues" target="_blank" rel="noopener noreferrer">Skapa en issue på GitHub →</a></p>
        </div>
        <div className="contact-card">
          <div className="icon">💡</div>
          <h3>Förslag och idéer</h3>
          <p>Har du förslag på nya funktioner eller förbättringar? Vi lyssnar gärna på dina idéer!</p>
          <p><a href="https://github.com/eoq746/SwedishCrossword/discussions" target="_blank" rel="noopener noreferrer">Diskutera på GitHub →</a></p>
        </div>
        <div className="contact-card">
          <div className="icon">📝</div>
          <h3>Bidra med ord</h3>
          <p>Vill du föreslå nya ord eller förbättra ledtrådar? Vi uppskattar alla bidrag till ordlistan.</p>
          <p><a href="https://github.com/eoq746/SwedishCrossword/discussions" target="_blank" rel="noopener noreferrer">Diskutera på GitHub →</a></p>
        </div>
      </div>

      <div className="guidelines-box">
        <h3>Riktlinjer för feedback</h3>
        <p>För att vi ska kunna hjälpa dig effektivt, försök inkludera följande information när du rapporterar problem:</p>
        <ul>
          <li>Datum för det korsord där problemet uppstod</li>
          <li>Vilken enhet och webbläsare du använder</li>
          <li>En beskrivning av vad som hände och vad du förväntade dig</li>
          <li>Skärmdumpar om det hjälper att förklara problemet</li>
        </ul>
        <p>Vid förslag på nya ord, inkludera gärna en källa eller definition så att vi kan verifiera att ordet är korrekt.</p>
      </div>

      <h2>Vanliga frågor (FAQ)</h2>
      <p>Innan du kontaktar oss, kolla om ditt svar finns bland våra vanliga frågor nedan:</p>

      <div className="faq-section">
        {FAQ_ITEMS.map(({ q, a }) => (
          <div className="faq-item" key={q}>
            <h3>{q}</h3>
            <p>{a}</p>
          </div>
        ))}
      </div>

      <Link to="/" className="back-link">← Startsida</Link>
    </>
  );
}
