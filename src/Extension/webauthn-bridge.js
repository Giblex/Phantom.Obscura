// Runs in the page's own JavaScript world so it can see navigator.credentials.
//
// Why a separate file and a separate world: content.js runs in an isolated world, where
// patching navigator.credentials would be invisible to the page. Only a MAIN-world script can
// stand in front of the real WebAuthn API. It therefore has no extension privileges at all —
// it cannot reach chrome.runtime — so everything it needs goes through window.postMessage to
// content.js, which does hold those privileges.
//
// The trust model that makes this safe to do at all:
//   * The page supplies the challenge; we never invent one.
//   * clientDataJSON is built here from the real origin, and the app hashes exactly those
//     bytes. A page cannot get a signature over data it did not send.
//   * The app independently checks that this origin may assert for the requested rpId, using
//     the origin the browser reports for the tab — not anything this script says.
//   * Attestor prompts for Windows Hello before signing, so a script calling get() in the
//     background cannot silently authenticate anyone.
//
// If anything at all goes wrong we fall through to the browser's own implementation rather
// than failing the ceremony: a site that has platform passkeys must keep working.

(() => {
  // Non-writable so the page cannot flip it back and provoke a second patch of
  // navigator.credentials.get, which would stack shims and double every request. A page CAN
  // set it before we run to opt out of the bridge, and that is harmless: the site simply gets
  // the browser's own authenticator, which is the same result as us declining.
  const BRIDGE_FLAG = '__phantomWebauthnBridge';
  if (window[BRIDGE_FLAG]) return;
  try {
    Object.defineProperty(window, BRIDGE_FLAG, {
      value: true, configurable: false, enumerable: false, writable: false
    });
  } catch {
    return; // already defined as non-configurable by someone else; do not patch twice
  }

  const credentials = navigator.credentials;
  if (!credentials || typeof credentials.get !== 'function') return;

  // A sandboxed iframe or about:blank has the opaque origin "null". postMessage would throw a
  // SyntaxError on that as a targetOrigin, and the app would refuse such a request anyway
  // because WebAuthn requires a secure context. Leave those documents entirely alone.
  const pageOrigin = window.location.origin;
  if (!pageOrigin || pageOrigin === 'null') return;

  const nativeGet = credentials.get.bind(credentials);

  const REQUEST = '__phantom_webauthn_request__';
  const RESPONSE = '__phantom_webauthn_response__';
  const TIMEOUT_MS = 120000; // the user has to answer a Windows Hello prompt

  let seq = 0;
  const pending = new Map();

  window.addEventListener('message', (event) => {
    if (event.source !== window) return;
    const msg = event.data;
    if (!msg || msg.type !== RESPONSE || typeof msg.id !== 'number') return;

    const resolve = pending.get(msg.id);
    if (!resolve) return;
    pending.delete(msg.id);
    resolve(msg.result || null);
  });

  function ask(payload) {
    return new Promise((resolve) => {
      const id = ++seq;
      pending.set(id, resolve);
      window.postMessage({ type: REQUEST, id, payload }, pageOrigin);
      setTimeout(() => {
        if (pending.delete(id)) resolve(null);
      }, TIMEOUT_MS);
    });
  }

  const toBase64Url = (buf) => {
    const bytes = new Uint8Array(buf);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  };

  const fromBase64Url = (value) => {
    const padded = value.replace(/-/g, '+').replace(/_/g, '/');
    const binary = atob(padded + '==='.slice((padded.length + 3) % 4));
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
  };

  credentials.get = async function get(options) {
    // Not a WebAuthn call (could be a federated or password credential request).
    if (!options || !options.publicKey) return nativeGet(options);

    // Conditional mediation is the passkey-autofill probe a site fires on page load. It is
    // speculative and expected to sit pending until the user picks an account, so answering it
    // eagerly would throw a Windows Hello prompt in the user's face on every page view. Leave
    // it to the browser.
    if (options.mediation === 'conditional') return nativeGet(options);

    // An already-aborted request must not raise a prompt either.
    if (options.signal?.aborted) return nativeGet(options);

    const publicKey = options.publicKey;

    let clientDataJson;
    try {
      clientDataJson = JSON.stringify({
        type: 'webauthn.get',
        challenge: toBase64Url(publicKey.challenge),
        origin: pageOrigin,
        crossOrigin: window.top !== window
      });
    } catch {
      return nativeGet(options);
    }

    const result = await ask({
      rpId: publicKey.rpId || window.location.hostname,
      clientDataJson
    });

    // No passkey of ours for this site, user declined, or the vault is locked. Hand the
    // ceremony back to the browser so a platform passkey can still answer it.
    if (!result) return nativeGet(options);

    // If the site named the credentials it will accept, honour that. The vault does not track
    // WebAuthn credential IDs, so it can pick a passkey the site did not ask for when more
    // than one is stored for the same domain. Returning that would fail the ceremony with no
    // way back — checking here means we can still fall through to the browser instead.
    if (Array.isArray(publicKey.allowCredentials) && publicKey.allowCredentials.length > 0) {
      const accepted = publicKey.allowCredentials.some(
        (c) => c && c.id && toBase64Url(c.id) === result.credentialId
      );
      if (!accepted) return nativeGet(options);
    }

    const rawId = fromBase64Url(result.credentialId);

    // Shaped like a PublicKeyCredential. It is not an instance of the real class — a content
    // script cannot mint one — but it carries every field a relying party reads, and
    // getClientExtensionResults/toJSON are present because libraries call them unguarded.
    return {
      id: result.credentialId,
      rawId,
      type: 'public-key',
      authenticatorAttachment: 'cross-platform',
      response: {
        clientDataJSON: new TextEncoder().encode(clientDataJson).buffer,
        authenticatorData: fromBase64Url(result.authenticatorData),
        signature: fromBase64Url(result.signature),
        userHandle: result.userHandle ? fromBase64Url(result.userHandle) : null
      },
      getClientExtensionResults: () => ({}),
      toJSON() {
        return {
          id: result.credentialId,
          rawId: result.credentialId,
          type: 'public-key',
          response: {
            clientDataJSON: toBase64Url(new TextEncoder().encode(clientDataJson)),
            authenticatorData: result.authenticatorData,
            signature: result.signature,
            userHandle: result.userHandle || null
          },
          clientExtensionResults: {}
        };
      }
    };
  };

  // Registration deliberately still goes to the browser.
  //
  // Creating a credential requires returning a CBOR attestation object with the new public key
  // in COSE form. Attestor can mint the key pair, but until that encoding exists here a
  // half-built response would leave the site believing a credential was registered that it
  // could never verify an assertion against — worse than not offering to register at all.
})();
