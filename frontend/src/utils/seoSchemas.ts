/**
 * Utilities for generating JSON-LD structured data for SEO and AI discovery.
 */

export function generateOrganizationSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: 'Svenskt Korsord',
    url: 'https://www.svensktkorsord.se',
    logo: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    description: 'Gratis dagliga svenska korsord online. Nytt pussel varje dag.',
    sameAs: [
      'https://www.facebook.com/svensktkorsord',
      'https://twitter.com/svensktkorsord',
      'https://www.instagram.com/svensktkorsord',
    ],
    contactPoint: {
      '@type': 'ContactPoint',
      contactType: 'Customer Support',
      email: 'contact@svensktkorsord.se',
    },
  };
}

export function generateWebsiteSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: 'Svenskt Korsord',
    url: 'https://www.svensktkorsord.se',
    description: 'Gratis dagliga svenska korsord online',
    potentialAction: {
      '@type': 'SearchAction',
      target: {
        '@type': 'EntryPoint',
        urlTemplate: 'https://www.svensktkorsord.se/calendar?search={search_term_string}',
      },
      'query-input': 'required name=search_term_string',
    },
  };
}

export function generateGameSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'Game',
    name: 'Svenskt Korsord',
    description: 'Gratis dagliga svenska korsord online. Testa ditt ordförråd och tävla på topplistan.',
    url: 'https://www.svensktkorsord.se',
    applicationCategory: 'Game',
    offers: {
      '@type': 'Offer',
      price: '0',
      priceCurrency: 'SEK',
    },
    author: {
      '@type': 'Organization',
      name: 'Svenskt Korsord',
    },
  };
}

export function generateFAQSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: [
      {
        '@type': 'Question',
        name: 'Vad är Svenskt Korsord?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: 'Svenskt Korsord är en gratis webbplats för att spela svenska korsord online. Nytt pussel publiceras varje dag.',
        },
      },
      {
        '@type': 'Question',
        name: 'Kostar Svenskt Korsord något?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: 'Nej, Svenskt Korsord är helt gratis att spela. Vi finansieras genom diskreta annonser.',
        },
      },
      {
        '@type': 'Question',
        name: 'Kan jag spela tidigare korsord?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: 'Ja, du kan se alla tidigare korsord i arkivet. Gå till Arkiv-sidan för att bläddra genom tidigare pussel.',
        },
      },
      {
        '@type': 'Question',
        name: 'Kan jag tävla på topplistan?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: 'Ja, när du löser ett korsord kan du se hur du placerar dig mot andra spelare idag på topplistan.',
        },
      },
    ],
  };
}

export function generateBreadcrumbSchema(items: Array<{ name: string; url: string }>) {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: items.map((item, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: item.name,
      item: item.url,
    })),
  };
}

export function generatePuzzleSchema(
  title: string,
  description: string,
  date: string,
  difficulty: string,
  solveTime?: number
) {
  return {
    '@context': 'https://schema.org',
    '@type': 'Game',
    name: title,
    description: description,
    datePublished: date,
    keywords: `svenska korsord, korsord, ordpussel, hjärnträning${difficulty ? `, ${difficulty}` : ''}`,
    author: {
      '@type': 'Organization',
      name: 'Svenskt Korsord',
    },
    ...(solveTime && {
      estimatedDuration: `PT${solveTime}M`,
    }),
  };
}

export function generateLeaderboardSchema() {
  return {
    '@context': 'https://schema.org',
    '@type': 'ItemList',
    name: 'Topplista - Dagens Snabbaste',
    description: 'Se de snabbaste lösarna av dagens korsord',
    itemListElement: [],
  };
}
