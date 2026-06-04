// Ambient declaration for Google gtag (Consent Mode v2 + general use).
// The actual script tag is added in index.html when AdSense is configured.
interface Window {
  gtag?: (
    command: 'consent' | 'config' | 'event' | 'js' | 'set',
    targetOrDate: string | Date,
    params?: Record<string, unknown>
  ) => void;
}
