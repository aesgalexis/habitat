// Consent is local to this extension installation. A desktop pairing key alone
// never enables reading page content or connecting to the desktop service.
const HABITAT_CONSENT_VERSION = 1;
async function habitatConnectionEnabled() {
  const saved = await chrome.storage.local.get(["habitatConsentVersion", "habitatDisabled"]);
  return saved.habitatConsentVersion === HABITAT_CONSENT_VERSION && !saved.habitatDisabled;
}
