import { Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

export default function PrivacyPolicyPage() {
  usePageTitle('Integritetspolicy');
  
  useSEO({
    title: 'Integritetspolicy',
    description: 'Läs Svenskt Korsords integritetspolicy. Lär dig hur vi skyddar din integritet och hanterar dina personuppgifter.',
    canonical: 'https://www.svensktkorsord.se/privacy-policy',
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    robots: 'noindex, follow',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Integritetspolicy', url: 'https://www.svensktkorsord.se/privacy-policy' }
    ])
  });

  return (
    <>
      <h1>Integritetspolicy</h1>
      <p className="last-updated">Senast uppdaterad: Mars 2026</p>

      <p>Välkommen till Svenskt Korsord. Vi värnar om din integritet och vill vara transparenta med hur vi hanterar information på vår webbplats. Denna integritetspolicy förklarar vilken information vi samlar in, hur vi använder den och vilka rättigheter du har.</p>

      <div className="highlight-box">
        <strong>Sammanfattning:</strong> Vi samlar in minimalt med information. Dina spelframsteg sparas lokalt i din webbläsare. Om du väljer att logga in med Google eller Microsoft sparas ett anonymiserat användar-ID, ditt valda alias och din vänlista på servern. Du kan när som helst <Link to="/profile">exportera dina uppgifter</Link> eller <Link to="/profile">radera ditt konto</Link> via din profilsida.
      </div>

      <h2>1. Vem är personuppgiftsansvarig?</h2>
      <p>Svenskt Korsord drivs som ett hobbyprojekt av enskilda utvecklare. För frågor om integritet kan du kontakta oss via <a href="https://github.com/eoq746/SwedishCrossword/discussions" target="_blank" rel="noopener noreferrer">GitHub Discussions</a>, vår <Link to="/contact">kontaktsida</Link>, eller e-post: <a href="mailto:jockeb14@gmail.com">jockeb14@gmail.com</a>.</p>

      <h2>2. Information vi samlar in</h2>
      <p>Svenskt Korsord är utformat för att minimera insamling av personuppgifter.</p>

      <h3>2.1 Information du aktivt lämnar</h3>
      <table className="data-table">
        <thead>
          <tr>
            <th>Typ av information</th>
            <th>Beskrivning</th>
            <th>Syfte</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Användarnamn för topplistan</td>
            <td>Frivilligt namn (max 20 tecken) vid lösning av korsord</td>
            <td>Visas på den delade topplistan</td>
          </tr>
          <tr>
            <td>Speltid</td>
            <td>Hur lång tid det tog att lösa korsordet</td>
            <td>Rankning på topplistan</td>
          </tr>
          <tr>
            <td>Inloggning (valfritt)</td>
            <td>Vid inloggning med Google eller Microsoft lagras ett anonymiserat (hashat) användar-ID — aldrig ditt riktiga ID</td>
            <td>Profil, statistik och vänfunktion</td>
          </tr>
          <tr>
            <td>Alias (valfritt)</td>
            <td>Ett självvalt alias (2–20 tecken) som du kan ändra när som helst</td>
            <td>Visas för vänner på vänners topplista</td>
          </tr>
          <tr>
            <td>Vänförfrågningar</td>
            <td>Om du lägger till vänner lagras vänrelationen på servern</td>
            <td>Vänners topplista på pusselsidan</td>
          </tr>
        </tbody>
      </table>

      <h3>2.2 Automatiskt insamlad information</h3>
      <table className="data-table">
        <thead>
          <tr>
            <th>Typ av information</th>
            <th>Beskrivning</th>
            <th>Lagring</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Spelframsteg</td>
            <td>Ifyllda bokstäver i korsordet</td>
            <td>Lokalt i din webbläsare (localStorage)</td>
          </tr>
          <tr>
            <td>Inställningar</td>
            <td>Eventuella preferenser du gjort</td>
            <td>Lokalt i din webbläsare (localStorage)</td>
          </tr>
          <tr>
            <td>Anonymiserad statistik</td>
            <td>Information för anti-fusk-systemet</td>
            <td>Lokalt i din webbläsare</td>
          </tr>
        </tbody>
      </table>

      <h3>2.3 Information via tredjepartstjänster</h3>
      <ul>
        <li><strong>Google AdSense:</strong> Visar annonser till utloggade användare efter cookie-val. Vid "Acceptera alla" kan personanpassade annonser visas. Vid "Endast nödvändiga" används icke-personanpassade annonser (NPA).</li>
        <li><strong>Azure Container Apps:</strong> Vår webbtjänst körs på Microsoft Azure, som kan logga anonymiserade åtkomstdata.</li>
      </ul>

      <h2>3. Cookies och lokal lagring</h2>

      <h3>3.1 LocalStorage (lokal lagring)</h3>
      <p>Vi använder webbläsarens localStorage för att spara dina spelframsteg, senaste speltid och tidsstämpel. LocalStorage-data skickas aldrig till våra servrar automatiskt.</p>

      <h3>3.2 Autentiseringscookie</h3>
      <p>Om du loggar in sätts en krypterad session-cookie (<code>.Crossword.Auth</code>) som håller dig inloggad i upp till 30 dagar. Cookien är HttpOnly och Secure — den kan inte läsas av JavaScript och skickas bara över HTTPS.</p>

      <h3>3.3 Samtyckeshantering för annonser</h3>
      <p>När du besöker sidan första gången väljer du mellan <strong>"Acceptera alla"</strong> och <strong>"Endast nödvändiga"</strong>. Innan valet görs laddas inga annonsskript.</p>
      <ul>
        <li><strong>Acceptera alla:</strong> Annonsskript kan laddas för personanpassade annonser.</li>
        <li><strong>Endast nödvändiga:</strong> Annonser visas i icke-personanpassat läge (NPA).</li>
        <li><strong>Inloggad användare:</strong> Vi visar inga annonser alls.</li>
      </ul>

      <h2>4. Google AdSense och tredjepartsannonser</h2>
      <p>Vi använder Google AdSense för att finansiera tjänsten. Google kan behandla teknisk information (IP-adress, enhets- och webbläsaruppgifter) när annonser levereras.</p>
      <div className="warning-box">
        <strong>Viktigt om annonser:</strong> Vid valet "Endast nödvändiga" används icke-personanpassade annonser (NPA). NPA innebär mindre profilering, men kan fortfarande innebära begränsad användning av cookies för frekvensbegränsning och bedrägeriskydd.
      </div>
      <p>Läs mer: <a href="https://www.google.com/policies/privacy/partners/" target="_blank" rel="noopener noreferrer">Hur Google använder uppgifter</a>. Hantera annonsinställningar via <a href="https://www.google.com/settings/ads" target="_blank" rel="noopener noreferrer">Googles annonsinställningar</a> eller <a href="https://www.youronlinechoices.eu/" target="_blank" rel="noopener noreferrer">Your Online Choices (EU)</a>.</p>

      <h2>5. Hur vi använder informationen</h2>
      <ul>
        <li><strong>Visa topplistan:</strong> Ditt valda användarnamn och speltid visas på den gemensamma topplistan.</li>
        <li><strong>Spara framsteg:</strong> Dina ifyllda svar sparas lokalt så att du kan fortsätta spela senare.</li>
        <li><strong>Förhindra fusk:</strong> Anonymiserad data används för att upptäcka misstänkt aktivitet på topplistan.</li>
        <li><strong>Visa annonser:</strong> Google AdSense visar annonser för utloggade användare baserat på valt samtyckesläge.</li>
      </ul>

      <h2>6. Delning av information</h2>
      <p>Vi delar inte din personliga information med tredje parter, förutom topplistan (publika användarnamn och tider), annonsrelaterad data via Google AdSense, och eventuella rättsliga krav.</p>

      <h2>7. Dina rättigheter enligt GDPR</h2>

      <h3>7.1 Rätt till tillgång och dataportabilitet</h3>
      <p>Du kan exportera alla dina serverlagrade uppgifter (statistik, poäng, alias, vänlista) i JSON-format via din <Link to="/profile">profilsida</Link>.</p>

      <h3>7.2 Rätt till radering</h3>
      <p>Du kan radera hela ditt konto via din <Link to="/profile">profilsida</Link>. Detta anonymiserar dina poäng och historik, tar bort ditt alias och alla vänrelationer, och loggar ut dig. Lokala data raderas genom att rensa webbläsarens cache.</p>

      <h3>7.3 Rätt att invända mot behandling</h3>
      <p>Du kan välja att inte ange ett användarnamn på topplistan, välja att inte logga in, och välja samtyckesläge för annonser via cookiebannern.</p>

      <h3>7.4 Rätt att klaga</h3>
      <p>Om du anser att vi behandlar dina personuppgifter på ett felaktigt sätt kan du lämna in ett klagomål till <a href="https://www.imy.se/" target="_blank" rel="noopener noreferrer">Integritetsskyddsmyndigheten (IMY)</a>.</p>

      <h2>8. Datalagring och gallring</h2>
      <ul>
        <li><strong>Topplistan (scores):</strong> Poäng äldre än 7 dagar gallras automatiskt.</li>
        <li><strong>Historik:</strong> Spelhistorik äldre än 365 dagar gallras automatiskt.</li>
        <li><strong>Autentiseringscookie:</strong> Utgår automatiskt efter 30 dagar.</li>
        <li><strong>Alias och vänrelationer:</strong> Sparas tills du raderar ditt konto.</li>
      </ul>

      <h2>9. Datasäkerhet</h2>
      <ul>
        <li>Webbplatsen använder HTTPS för säker kommunikation.</li>
        <li>Inloggnings-ID:n hashas med SHA-256 — vi lagrar aldrig ditt riktiga Google/Microsoft-ID.</li>
        <li>Vi samlar inte in lösenord, betalningsinformation, e-postadresser eller andra känsliga uppgifter på servern.</li>
      </ul>

      <h2>10. Barns integritet</h2>
      <p>Vår tjänst riktar sig inte specifikt till barn under 13 år. Vi samlar inte medvetet in personlig information från barn.</p>

      <h2>11. Internationella dataöverföringar</h2>
      <p>Eftersom vi använder Google AdSense kan viss information överföras till servrar utanför EU/EES. Google använder standardavtalsklausuler enligt sina villkor.</p>

      <h2>12. Ändringar i denna policy</h2>
      <p>Vi kan uppdatera denna integritetspolicy från tid till annan. Eventuella ändringar publiceras på denna sida med ett uppdaterat datum.</p>

      <h2>13. Kontakt</h2>
      <p>Kontakta oss via <a href="https://github.com/eoq746/SwedishCrossword/discussions" target="_blank" rel="noopener noreferrer">GitHub Discussions</a>, vår <Link to="/contact">kontaktsida</Link>, eller e-post: <a href="mailto:jockeb14@gmail.com">jockeb14@gmail.com</a>.</p>

      <Link to="/" className="back-link">← Startsida</Link>
    </>
  );
}
