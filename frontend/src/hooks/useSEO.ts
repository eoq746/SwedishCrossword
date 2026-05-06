import { useEffect } from 'react';

export interface SEOMetadata {
  title?: string;
  description?: string;
  canonical?: string;
  ogType?: string;
  ogImage?: string;
  ogLocale?: string;
  twitterCard?: string;
  structuredData?: Record<string, unknown>;
  keywords?: string;
  robots?: string;
}

/**
 * Manages page-level SEO metadata including title, description, and structured data.
 * For pages without specific SEO params, the base index.html values are used.
 */
export function useSEO(metadata?: SEOMetadata) {
  useEffect(() => {
    if (!metadata) return;

    // Update title
    if (metadata.title) {
      document.title = `${metadata.title} – Svenskt Korsord`;
    }

    // Update or create meta tags
    const updateMetaTag = (name: string, content: string, property = false) => {
      const attr = property ? 'property' : 'name';
      let tag = document.querySelector(`meta[${attr}="${name}"]`) as HTMLMetaElement;
      if (!tag) {
        tag = document.createElement('meta');
        tag.setAttribute(attr, name);
        document.head.appendChild(tag);
      }
      tag.content = content;
    };

    if (metadata.description) {
      updateMetaTag('description', metadata.description);
      updateMetaTag('og:description', metadata.description, true);
      updateMetaTag('twitter:description', metadata.description);
    }

    if (metadata.canonical) {
      let canonical = document.querySelector('link[rel="canonical"]') as HTMLLinkElement;
      if (!canonical) {
        canonical = document.createElement('link');
        canonical.rel = 'canonical';
        document.head.appendChild(canonical);
      }
      canonical.href = metadata.canonical;
    }

    if (metadata.ogImage) {
      updateMetaTag('og:image', metadata.ogImage, true);
      updateMetaTag('twitter:image', metadata.ogImage);
    }

    if (metadata.ogType) {
      updateMetaTag('og:type', metadata.ogType, true);
    }

    if (metadata.twitterCard) {
      updateMetaTag('twitter:card', metadata.twitterCard);
    }

    if (metadata.keywords) {
      updateMetaTag('keywords', metadata.keywords);
    }

    if (metadata.robots) {
      updateMetaTag('robots', metadata.robots);
    }

    // Update structured data
    if (metadata.structuredData) {
      let scriptTag = document.querySelector('script[type="application/ld+json"][data-type="page-schema"]') as HTMLScriptElement;
      if (!scriptTag) {
        scriptTag = document.createElement('script');
        scriptTag.type = 'application/ld+json';
        scriptTag.setAttribute('data-type', 'page-schema');
        document.head.appendChild(scriptTag);
      }
      scriptTag.textContent = JSON.stringify(metadata.structuredData);
    }

    // Update OG title and Twitter title
    if (metadata.title) {
      updateMetaTag('og:title', metadata.title, true);
      updateMetaTag('twitter:title', metadata.title);
    }

    return () => {
      // Cleanup: reset to base values from index.html
      // (optional; most pages will maintain their SEO regardless)
    };
  }, [metadata]);
}
