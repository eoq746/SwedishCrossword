import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import type { Plugin } from 'vite';

/**
 * Stamps nonce="__CSP_NONCE__" on every <script> and <link rel="stylesheet">
 * tag emitted into index.html at build time. Program.cs replaces the placeholder
 * with a real per-request nonce before serving the file, matching the same
 * __CSP_NONCE__ substitution used for the legacy HTML pages.
 */
function cspNoncePlaceholderPlugin(): Plugin {
  return {
    name: 'csp-nonce-placeholder',
    transformIndexHtml(html) {
      return html
        // <script ...> — add nonce before the closing >
        .replace(/(<script\b[^>]*)(>)/g, '$1 nonce="__CSP_NONCE__"$2')
        // <link rel="stylesheet" ...> — add nonce before /? >
        .replace(/(<link\b[^>]*\brel=["']stylesheet["'][^>]*?)(\/?>)/g, '$1 nonce="__CSP_NONCE__"$2');
    },
  };
}

export default defineConfig({
  base: '/app/',
  build: {
    outDir: '../SwedishCrossword.Api/wwwroot/app',
    emptyOutDir: true,
  },
  plugins: [react(), cspNoncePlaceholderPlugin()],
  server: {
    // Proxy API calls to the .NET backend during local development.
    // Run `dotnet run` in SwedishCrossword.Api and `npm run dev` in frontend/
    // at the same time; all /api/* requests are forwarded transparently.
    //
    // The wwwroot entries below proxy the static assets that are referenced as
    // absolute URLs in index.html (e.g. /tokens.css, /site.min.css). These files
    // live in wwwroot/ and are only served by the .NET host — without these entries
    // the Vite dev server would 404 them, causing the app to launch with no styles.
    proxy: {
      '/api': {
        target: 'http://localhost:50580',
        changeOrigin: false,
      },
      // wwwroot static assets referenced from index.html
      '/tokens.css':         { target: 'http://localhost:50580', changeOrigin: false },
      '/site.min.css':       { target: 'http://localhost:50580', changeOrigin: false },
      '/site.webmanifest':   { target: 'http://localhost:50580', changeOrigin: false },
      '/favicon-32x32.png':  { target: 'http://localhost:50580', changeOrigin: false },
      '/favicon-16x16.png':  { target: 'http://localhost:50580', changeOrigin: false },
      '/apple-touch-icon.png': { target: 'http://localhost:50580', changeOrigin: false },
    },
  },
});
