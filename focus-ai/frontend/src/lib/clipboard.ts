/**
 * Copy text to the clipboard, including where the modern API does not exist.
 *
 * `navigator.clipboard` is gated behind a secure context, and this app is read
 * over plain HTTP on a Tailscale address — the same reason the service worker
 * never registers on the phone. So on the one device this feature was built for,
 * the modern path is simply absent, and a button that quietly did nothing would
 * be the whole feature failing at its last step.
 *
 * Hence the ladder: the real API when it is there, the deprecated
 * `execCommand('copy')` when it is not, and an explicit failure when neither
 * works so the caller can fall back to showing selectable text. Never a silent
 * success.
 */
export async function copyText(text: string): Promise<boolean> {
  if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Permission denied or a non-secure context that still exposed the object.
      // Fall through rather than reporting a copy that did not happen.
    }
  }

  return legacyCopy(text);
}

/**
 * The pre-Clipboard-API route. Works without a secure context, which is exactly
 * when it is needed.
 */
function legacyCopy(text: string): boolean {
  if (typeof document === 'undefined') return false;

  const field = document.createElement('textarea');
  field.value = text;

  // Off-screen rather than hidden: a display:none or visibility:hidden element
  // cannot hold a selection, so the copy would fail. It also must not be
  // readOnly on iOS, which refuses to select a read-only field.
  field.setAttribute('aria-hidden', 'true');
  field.style.position = 'fixed';
  field.style.top = '-1000px';
  field.style.left = '0';
  field.style.opacity = '0';

  document.body.appendChild(field);

  try {
    field.focus();
    field.select();
    // iOS ignores select() on a textarea and needs the range set explicitly.
    field.setSelectionRange(0, text.length);

    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    document.body.removeChild(field);
  }
}
