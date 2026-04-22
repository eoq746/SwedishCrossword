/*
 * Cookie consent + ad loading.
 *
 * Current strategy (Option A — NPA fallback):
 *   - Signed in                      => no ads at all (sign-in incentive).
 *   - Not signed in + 'all'          => load AdSense with personalized ads.
 *   - Not signed in + 'essential'    => load AdSense in non-personalized mode
 *                                       (NPA: no profiling, contextual only).
 *   - Not signed in + no answer yet  => no ads until the user picks an option.
 *
 * Note: NPA still sets a small number of cookies/identifiers (frequency capping,
 * fraud prevention). Most EU DPAs treat these as requiring consent, so this is
 * a pragmatic middle ground rather than a strictly compliant solution.
 *
 * Future improvement (Option B — Google Consent Mode v2):
 *   Load gtag + AdSense on every page from the start, push consent signals
 *   ('default' denied, 'update' on banner choice) and let Google decide
 *   personalized vs non-personalized vs cookieless pings internally. This is
 *   Google's officially recommended path and is required for full EEA/UK
 *   monetization under the Digital Markets Act, but it's a larger change
 *   (gtag wiring, privacy-policy updates, ads loading in 'denied' state
 *   before the user clicks anything).
 */
(function () {
    'use strict';

    var CONSENT_KEY = 'cookie_consent';
    var AUTH_CACHE_KEY = 'auth_signed_in';
    var ADS_CATEGORY = 'ads';

    function getConsent() {
        try { return localStorage.getItem(CONSENT_KEY); } catch (e) { return null; }
    }

    function setConsent(value) {
        try { localStorage.setItem(CONSENT_KEY, value); } catch (e) { /* noop */ }
    }

    /**
     * Last-known authentication state, cached in sessionStorage so we can decide
     * synchronously on page load whether to load ads. Refreshed asynchronously
     * via /api/auth/me on every page load.
     */
    function isAuthenticatedCached() {
        try { return sessionStorage.getItem(AUTH_CACHE_KEY) === '1'; } catch (e) { return false; }
    }

    function setAuthenticatedCached(authed) {
        try { sessionStorage.setItem(AUTH_CACHE_KEY, authed ? '1' : '0'); } catch (e) { /* noop */ }
    }

    function refreshAuthState() {
        // Fire-and-forget; updates the cache for future page loads.
        try {
            fetch('/api/auth/me', { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    var authed = !!(data && data.authenticated);
                    var was = isAuthenticatedCached();
                    setAuthenticatedCached(authed);
                    // If the user just signed out in this tab, allow ads to load now.
                    if (was && !authed) {
                        applyAdPolicy();
                    }
                })
                .catch(function () { /* offline / ignore */ });
        } catch (e) { /* ignore */ }
    }

    function removeBanner() {
        var el = document.getElementById('cookie-consent-banner');
        if (el) el.remove();
    }

    function onAccept() {
        setConsent('all');
        removeBanner();
        applyAdPolicy();
    }

    function onRejectNonEssential() {
        setConsent('essential');
        removeBanner();
        applyAdPolicy();
    }

    /**
     * Activates <script type="text/plain" data-consent-src="..."> placeholders
     * by replacing them with real, executable <script> tags. The optional
     * `mode` argument controls how 'ads'-category placeholders are handled:
     *   - 'personalized' (default): load normally.
     *   - 'npa'                  : load, but first push the non-personalized
     *                              flag onto the adsbygoogle queue.
     *   - 'skip'                 : don't load ads at all.
     * Non-ads placeholders are always loaded.
     */
    // Allowlist of origins that may host scripts activated via the
    // data-consent-src placeholder mechanism. Validating the URL here prevents
    // a tampered placeholder from injecting a script from an arbitrary origin
    // (and silences CodeQL's "DOM text reinterpreted as HTML" warning, which
    // treats DOM attribute values as untrusted input).
    var ALLOWED_SCRIPT_ORIGINS = [
        'https://pagead2.googlesyndication.com'
    ];

    function isAllowedScriptUrl(rawUrl) {
        if (!rawUrl) { return false; }
        try {
            var parsed = new URL(rawUrl, document.baseURI);
            if (parsed.protocol !== 'https:') { return false; }
            for (var i = 0; i < ALLOWED_SCRIPT_ORIGINS.length; i++) {
                if (parsed.origin === ALLOWED_SCRIPT_ORIGINS[i]) { return true; }
            }
        } catch (e) { /* fallthrough */ }
        return false;
    }

    function loadConsentedScripts(mode) {
        mode = mode || 'personalized';
        var placeholders = document.querySelectorAll(
            'script[type="text/plain"][data-consent-src]:not([data-consent-loaded])'
        );
        var npaPushed = false;
        for (var i = 0; i < placeholders.length; i++) {
            var oldScript = placeholders[i];
            var isAds = oldScript.getAttribute('data-consent-category') === ADS_CATEGORY;

            if (isAds && mode === 'skip') { continue; }

            var src = oldScript.getAttribute('data-consent-src');
            if (!isAllowedScriptUrl(src)) {
                // Refuse to activate placeholders that point at an
                // unexpected origin. Mark as loaded so we don't retry.
                oldScript.setAttribute('data-consent-loaded', 'true');
                oldScript.setAttribute('data-consent-mode', 'blocked');
                continue;
            }

            if (isAds && mode === 'npa' && !npaPushed) {
                // Tell AdSense to serve non-personalized ads. Must be queued
                // BEFORE the loader script executes.
                try {
                    window.adsbygoogle = window.adsbygoogle || [];
                    window.adsbygoogle.requestNonPersonalizedAds = 1;
                    window.adsbygoogle.pauseAdRequests = 0;
                } catch (e) { /* ignore */ }
                npaPushed = true;
            }

            var newScript = document.createElement('script');
            newScript.src = src;
            if (oldScript.getAttribute('data-consent-async') === 'true') {
                newScript.async = true;
            }
            var crossorigin = oldScript.getAttribute('data-consent-crossorigin');
            if (crossorigin) {
                newScript.crossOrigin = crossorigin;
            }
            oldScript.setAttribute('data-consent-loaded', 'true');
            oldScript.setAttribute('data-consent-mode', isAds ? mode : 'always');
            oldScript.parentNode.insertBefore(newScript, oldScript.nextSibling);
        }
    }

    /**
     * Decides which ad mode to use based on auth state + cookie consent and
     * activates the corresponding placeholders. Safe to call multiple times;
     * already-loaded placeholders are not re-injected.
     */
    function applyAdPolicy() {
        var consent = getConsent();
        var signedIn = isAuthenticatedCached();
        var mode;

        if (signedIn) {
            mode = 'skip';                  // Ad-free for logged-in users.
        } else if (consent === 'all') {
            mode = 'personalized';
        } else if (consent === 'essential') {
            mode = 'npa';                   // Non-personalized fallback.
        } else {
            mode = 'skip';                  // No answer yet — wait for banner.
        }

        var run = function () { loadConsentedScripts(mode); };
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', run);
        } else {
            run();
        }
    }

    function showBanner() {
        var banner = document.createElement('div');
        banner.id = 'cookie-consent-banner';
        banner.setAttribute('role', 'dialog');
        banner.setAttribute('aria-label', 'Cookie-samtycke');
        banner.innerHTML =
            '<div class="cookie-consent-inner">' +
                '<p>Vi använder cookies för att förbättra din upplevelse. Nödvändiga cookies krävs för att sidan ska fungera. ' +
                'Läs mer i vår <a href="/privacy-policy.html">integritetspolicy</a>.</p>' +
                '<div class="cookie-consent-buttons">' +
                    '<button id="cookie-accept-all" class="cookie-btn cookie-btn-accept">Acceptera alla</button>' +
                    '<button id="cookie-reject" class="cookie-btn cookie-btn-reject">Endast nödvändiga</button>' +
                '</div>' +
            '</div>';
        document.body.appendChild(banner);

        document.getElementById('cookie-accept-all').addEventListener('click', onAccept);
        document.getElementById('cookie-reject').addEventListener('click', onRejectNonEssential);
    }

    if (!getConsent()) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', showBanner);
        } else {
            showBanner();
        }
    }
    // Always run the ad policy on load — it picks the right mode (personalized,
    // NPA, or skip) based on consent + auth state.
    applyAdPolicy();

    // Hide any rendered ad slots for signed-in users (defensive — currently no
    // live <ins class="adsbygoogle"> slots, but this future-proofs the perk).
    (function injectAdHidingStyles() {
        try {
            var style = document.createElement('style');
            style.textContent =
                'html.user-signed-in .adsbygoogle,' +
                'html.user-signed-in [data-ad-client],' +
                'html.user-signed-in .ad-slot { display: none !important; }';
            (document.head || document.documentElement).appendChild(style);
            if (isAuthenticatedCached()) {
                document.documentElement.classList.add('user-signed-in');
            }
        } catch (e) { /* ignore */ }
    })();

    // Refresh auth state in the background so future page loads have an
    // up-to-date cached value when deciding whether to load ads.
    refreshAuthState();

    /**
     * Returns true if the user has accepted non-essential cookies.
     * Use this to gate any analytics, tracking, or third-party scripts:
     *
     *   if (window.CookieConsent.allowsAll()) { loadAnalytics(); }
     */
    window.CookieConsent = {
        allowsAll: function () { return getConsent() === 'all'; },
        allowsEssentialOnly: function () { return getConsent() === 'essential'; },
        hasResponded: function () { return getConsent() !== null; },
        reset: function () { try { localStorage.removeItem(CONSENT_KEY); } catch (e) {} }
    };
})();
