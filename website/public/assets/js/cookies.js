/* The cookie banner, and the consent gate in front of Google Analytics.

   Analytics used to load from layout.html on every page view, before anybody had
   answered the banner. The banner only had an "Accept" button, so there was no answer
   to wait for and no way to say no. Under the GDPR an analytics cookie is not strictly
   necessary, so it needs consent that the visitor can refuse and can take back. This
   file is therefore the only place that starts analytics: layout.html hands over the
   measurement ID as data, and the tag is fetched after a "yes" and never before. */

const CONSENT_COOKIE = "cookiesAccepted";

// Long enough that we are not re-asking a regular player every week, short enough that
// consent is a recent decision rather than a permanent one.
const CONSENT_DAYS = 180;

function setCookie(name, value, days) {
    const expiry = new Date(Date.now() + days * 24 * 60 * 60 * 1000);
    // SameSite blocks the cookie from riding along on cross-site requests, and Secure
    // keeps it off plain HTTP. Secure is conditional so that a local http://127.0.0.1
    // instance can still store it.
    const secure = location.protocol === "https:" ? ";Secure" : "";
    document.cookie = `${name}=${encodeURIComponent(value)};expires=${expiry.toUTCString()};path=/;SameSite=Lax${secure}`;
}

function getCookie(name) {
    const match = document.cookie
        .split(";")
        .map(part => part.trim())
        .find(part => part.startsWith(`${name}=`));
    return match ? decodeURIComponent(match.slice(name.length + 1)) : "";
}

/* Reads as a tri-state on purpose: "yes", "no", or unanswered. The old version compared
   the stored string against the boolean false, which only did the right thing because
   "" == false happens to be true in JavaScript. */
function consent() {
    return getCookie(CONSENT_COOKIE);
}

function startAnalytics() {
    const tag = document.getElementById("analyticsId");
    if (!tag) return;

    const id = JSON.parse(tag.textContent);
    if (!id || window.dataLayer) return;

    const script = document.createElement("script");
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`;
    document.head.appendChild(script);

    window.dataLayer = window.dataLayer || [];
    window.gtag = function () {
        window.dataLayer.push(arguments);
    };
    window.gtag("js", new Date());
    window.gtag("config", id);
}

function renderCookieHeader() {
    const banner = document.getElementById("gdprheader");
    if (banner) banner.style.display = consent() ? "none" : "flex";
}

function answerCookies(answer) {
    setCookie(CONSENT_COOKIE, answer, CONSENT_DAYS);
    renderCookieHeader();
    if (answer === "yes") startAnalytics();
}

document.getElementById("cookiesAccept")?.addEventListener("click", () => answerCookies("yes"));
document.getElementById("cookiesReject")?.addEventListener("click", () => answerCookies("no"));

renderCookieHeader();
if (consent() === "yes") startAnalytics();
