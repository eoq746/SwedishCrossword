import { useEffect, useState } from 'react';

/**
 * Tracks whether the backend is temporarily unavailable (HTTP 503 with
 * code "db_unavailable"). Mirrors the global fetch interceptor in site.js
 * for the React app shell.
 */
export function useDbStatus(): boolean {
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    const original = window.fetch.bind(window);

    window.fetch = async function patchedFetch(...args: Parameters<typeof fetch>) {
      const response = await original(...args);
      if (response.status === 503) {
        // Clone before reading body so callers can still consume it
        const clone = response.clone();
        clone.json().then((body: unknown) => {
          if (body && typeof body === 'object' && 'code' in body && (body as Record<string, unknown>).code === 'db_unavailable') {
            setUnavailable(true);
          }
        }).catch(() => {/* non-JSON 503 — ignore */});
      }
      return response;
    };

    return () => {
      window.fetch = original;
    };
  }, []);

  return unavailable;
}
