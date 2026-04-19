(function () {
    'use strict';

    var CONSENT_KEY = 'cookie_consent';

    function getConsent() {
        try { return localStorage.getItem(CONSENT_KEY); } catch (e) { return null; }
    }

    function setConsent(value) {
        try { localStorage.setItem(CONSENT_KEY, value); } catch (e) { /* noop */ }
    }

    function removeBanner() {
        var el = document.getElementById('cookie-consent-banner');
        if (el) el.remove();
    }

    function onAccept() {
        setConsent('all');
        removeBanner();
    }

    function onRejectNonEssential() {
        setConsent('essential');
        removeBanner();
    }

    function showBanner() {
        var banner = document.createElement('div');
        banner.id = 'cookie-consent-banner';
        banner.setAttribute('role', 'dialog');
        banner.setAttribute('aria-label', 'Cookie-samtycke');
        banner.innerHTML =
            '<div class="cookie-consent-inner">' +
                '<p>Vi använder cookies för att förbättra din upplevelse. Nödvändiga cookies krävs för att sidan ska fungera. ' +
                'Läs mer i vår <a href="/integritetspolicy.html">integritetspolicy</a>.</p>' +
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
