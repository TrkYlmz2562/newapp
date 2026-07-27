/**
 * Which device this is, to the extent the browser will admit it.
 *
 * Used only to phrase instructions — "Ayarlar › Erişilebilirlik" is help, "look
 * in your settings" is not — and never to gate behaviour. Feature detection
 * decides what the app does; this decides how it explains itself.
 */

export type Platform = 'ios' | 'android' | 'macos' | 'windows' | 'unknown';

export function detectPlatform(): Platform {
  if (typeof navigator === 'undefined') return 'unknown';

  const agent = navigator.userAgent;

  // iPadOS reports itself as a Mac; the touch points are what separate them.
  if (/iPhone|iPod/.test(agent)) return 'ios';
  if (/iPad/.test(agent) || (/Macintosh/.test(agent) && navigator.maxTouchPoints > 1)) return 'ios';
  if (/Android/.test(agent)) return 'android';
  if (/Macintosh/.test(agent)) return 'macos';
  if (/Windows/.test(agent)) return 'windows';

  return 'unknown';
}

/**
 * True where the browser is handed a deliberately reduced voice list.
 *
 * Apple's position, stated by a frameworks engineer on the developer forums, is
 * that "with Web Speech APIs only the pre-installed voices are available;
 * optionally downloadable voices are not available". So the Siri voices and the
 * Enhanced and Premium downloads a reader can see in Settings — the ones worth
 * having — never reach `getVoices()`, and Turkish arrives as the single compact
 * voice. Safari on macOS is subject to the same restriction; Chrome and Edge on
 * macOS are not.
 *
 * This is why the app can show one Turkish voice while the device's own
 * settings list several, and a reader who installs a better one sees nothing
 * change. Worth saying out loud rather than letting them hunt.
 */
export function hasRestrictedVoiceList(platform: Platform): boolean {
  if (platform === 'ios') return true;

  return (
    platform === 'macos' &&
    typeof navigator !== 'undefined' &&
    /Safari/.test(navigator.userAgent) &&
    !/Chrome|Chromium|Edg/.test(navigator.userAgent)
  );
}
