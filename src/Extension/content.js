/**
 * PhantomVault Autofill — content script.
 *
 * Detects login/registration forms, sends field metadata to the background
 * script (which relays to the native host), and injects a suggestion chip
 * that fills credentials when clicked.
 */

(function () {
  'use strict';

  // Per-session random tokens so page scripts cannot fingerprint our presence
  // by probing fixed property names or element IDs.
  const sessionToken = (() => {
    const bytes = new Uint8Array(16);
    (self.crypto || window.crypto).getRandomValues(bytes);
    return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
  })();
  const injectedFlag = `__pv_${sessionToken}`;
  const chipElementId = `__pv_chip_${sessionToken}`;

  if (window[injectedFlag]) return;
  Object.defineProperty(window, injectedFlag, { value: true, configurable: false, enumerable: false, writable: false });


  const PASSWORD_HINTS = ['password', 'passwd', 'pass', 'pwd'];
  const USERNAME_HINTS = ['username', 'user', 'login', 'account', 'email', 'e-mail', 'mail'];
  const TOTP_HINTS = ['2fa', 'mfa', 'otp', 'token', 'code', 'verification', 'auth-code', 'totp'];

  function classifyInput(el) {
    const attrs = [el.type, el.id, el.name, el.placeholder, el.autocomplete]
      .map(v => (v || '').toLowerCase());
    const combined = attrs.join(' ');

    if (el.type === 'password') return 'password';

    // WebAuthn advertises itself through autocomplete, so this has to be checked
    // before the username hints — a passkey field is very often also named
    // "username" for conditional-UI autofill.
    if ((el.autocomplete || '').toLowerCase().includes('webauthn')) return 'passkey';

    if (TOTP_HINTS.some(h => combined.includes(h))) return 'totp';
    if (el.type === 'email') return 'email';
    if (USERNAME_HINTS.some(h => combined.includes(h))) return 'username';
    return null;
  }

  /**
   * Fields that get a persistent icon.
   *
   * TOTP fields are excluded on purpose: the getCredentials payload carries no
   * one-time code, so an icon there would open a menu that cannot fill anything.
   * An affordance that does nothing is worse than none. Re-enable this once the
   * native host returns a code (see AutoFillField.TotpCode, which already exists
   * on the desktop side).
   */
  function isCredentialField(el) {
    if (!el || el.tagName !== 'INPUT') return false;
    if (el.disabled || el.readOnly) return false;
    const kind = classifyInput(el);
    return kind !== null && kind !== 'totp';
  }

  function collectFormFields(form) {
    const inputs = Array.from(form.querySelectorAll('input'));
    return inputs
      .map(el => ({
        selector: buildSelector(el),
        type: el.type,
        id: el.id,
        name: el.name,
        placeholder: el.placeholder,
        autocomplete: el.autocomplete,
        fieldClass: classifyInput(el)
      }))
      .filter(f => f.fieldClass !== null);
  }

  function buildSelector(el) {
    if (el.id) return `#${CSS.escape(el.id)}`;
    if (el.name) return `[name="${CSS.escape(el.name)}"]`;
    // Fallback: positional
    const parent = el.closest('form') || document.body;
    const inputs = Array.from(parent.querySelectorAll('input'));
    const idx = inputs.indexOf(el);
    return `form input:nth-of-type(${idx + 1})`;
  }

  function hasPasswordField(fields) {
    return fields.some(f => f.fieldClass === 'password');
  }


  // ── Persistent per-field icon ───────────────────────────────────────────────
  //
  // Every recognised credential field gets its own small icon pinned to its right
  // edge. Previously the only affordance was the suggestion chip, which popped up
  // by itself on the password field and then never came back once dismissed —
  // there was no way to ask for credentials on a username, passkey or 2FA field
  // at all. The icon is the persistent entry point; the chip is now only ever
  // opened by clicking one.

  const iconHostId = `__pv_icon_${sessionToken}`;
  const fieldIcons = new Map();   // input element -> host element
  let cachedCredentials = [];

  // Inline SVG so there is no extension-resource URL for a page to probe, which
  // would defeat the randomised element ids.
  const ICON_SVG =
    '<svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true">' +
    '<defs><linearGradient id="g" x1="1" y1="0" x2="0" y2="1">' +
    '<stop offset="0" stop-color="#3D7EE8"/><stop offset="0.5" stop-color="#2FA8DA"/>' +
    '<stop offset="1" stop-color="#19C4B0"/></linearGradient></defs>' +
    '<circle cx="12" cy="12" r="9" fill="none" stroke="url(#g)" stroke-width="2"/>' +
    '<circle cx="12" cy="12" r="3.2" fill="url(#g)"/></svg>';

  function attachFieldIcon(input) {
    if (!isCredentialField(input) || fieldIcons.has(input)) return;

    const host = document.createElement('div');
    host.id = iconHostId;
    host.style.cssText = 'position:absolute;z-index:2147483646;width:22px;height:22px;pointer-events:auto;';

    const shadow = host.attachShadow({ mode: 'closed' });
    const btn = document.createElement('div');
    btn.setAttribute('role', 'button');
    btn.setAttribute('tabindex', '-1');
    btn.setAttribute('aria-label', 'PhantomVault — fill a saved credential');
    btn.title = 'PhantomVault';
    btn.style.cssText =
      'width:22px;height:22px;border-radius:11px;display:flex;align-items:center;' +
      'justify-content:center;cursor:pointer;opacity:.72;' +
      'background:rgba(18,28,46,.86);border:1px solid #2C3B52;' +
      'transition:opacity .15s ease, transform .15s ease;';
    btn.innerHTML = ICON_SVG;
    btn.onmouseenter = () => { btn.style.opacity = '1'; btn.style.transform = 'scale(1.08)'; };
    btn.onmouseleave = () => { btn.style.opacity = '.72'; btn.style.transform = 'scale(1)'; };

    // mousedown, not click: the field must not lose focus before we fill it.
    btn.addEventListener('mousedown', (e) => {
      e.preventDefault();
      e.stopPropagation();
      openChipFor(input);
    });

    shadow.appendChild(btn);
    document.body.appendChild(host);
    fieldIcons.set(input, host);
    positionIcon(input, host);
  }

  function positionIcon(input, host) {
    // Detached or hidden fields keep their icon in the map but off-screen, so a
    // field that reappears (SPA step changes) does not need re-attaching.
    if (!input.isConnected) { detachFieldIcon(input); return; }

    const rect = input.getBoundingClientRect();
    const visible = rect.width > 0 && rect.height > 0 &&
      window.getComputedStyle(input).visibility !== 'hidden';

    if (!visible) { host.style.display = 'none'; return; }

    host.style.display = 'block';
    host.style.top = `${rect.top + window.scrollY + (rect.height - 22) / 2}px`;
    host.style.left = `${rect.left + window.scrollX + rect.width - 22 - 6}px`;
  }

  function detachFieldIcon(input) {
    const host = fieldIcons.get(input);
    if (host) host.remove();
    fieldIcons.delete(input);
  }

  function repositionAllIcons() {
    fieldIcons.forEach((host, input) => positionIcon(input, host));
  }

  function openChipFor(input) {
    if (cachedCredentials.length === 0) {
      // Vault may have unlocked since the last scan.
      requestCredentials(() => showSuggestionChip(input, cachedCredentials));
      return;
    }
    showSuggestionChip(input, cachedCredentials);
  }

  function requestCredentials(done) {
    try {
      chrome.runtime.sendMessage({ type: 'getCredentials', data: { domain: location.hostname } }, (resp) => {
        if (resp && resp.success && resp.data && Array.isArray(resp.data.credentials)) {
          cachedCredentials = resp.data.credentials;
        }
        if (typeof done === 'function') done();
      });
    } catch (err) {
      if (typeof done === 'function') done();
    }
  }

  // Layout changes constantly on real pages, so keep positions live rather than
  // placing the icons once and letting them drift.
  window.addEventListener('scroll', repositionAllIcons, { passive: true, capture: true });
  window.addEventListener('resize', repositionAllIcons, { passive: true });


  let activeChip = null;

  function showSuggestionChip(passwordInput, credentials) {
    removeChip();

    if (!credentials || credentials.length === 0) return;

    const rect = passwordInput.getBoundingClientRect();
    // Host carries only positioning; all content lives in a closed shadow root
    // so page scripts cannot read or fingerprint the chip's internals.
    const host = document.createElement('div');
    host.id = chipElementId;
    host.style.cssText = `
      position: fixed;
      z-index: 2147483647;
      top: ${rect.bottom + window.scrollY + 4}px;
      left: ${rect.left + window.scrollX}px;
    `;
    const shadow = host.attachShadow({ mode: 'closed' });

    const chip = document.createElement('div');
    chip.style.cssText = `
      background: #1a1a2e;
      border: 1px solid #4a4a8a;
      border-radius: 8px;
      padding: 6px 4px;
      box-shadow: 0 4px 16px rgba(0,0,0,0.4);
      font-family: system-ui, sans-serif;
      font-size: 13px;
      min-width: 220px;
      max-width: 360px;
      cursor: default;
    `;

    const header = document.createElement('div');
    header.style.cssText = 'color:#8888cc;font-size:11px;padding:2px 8px 4px;letter-spacing:.05em;';
    header.textContent = 'PHANTOMVAULT';
    chip.appendChild(header);

    credentials.slice(0, 3).forEach(cred => {
      const row = document.createElement('div');
      row.style.cssText = `
        display:flex;align-items:center;gap:8px;padding:6px 8px;
        border-radius:6px;color:#e0e0ff;cursor:pointer;
      `;
      row.onmouseenter = () => { row.style.background = '#2a2a5a'; };
      row.onmouseleave = () => { row.style.background = ''; };

      const icon = document.createElement('span');
      icon.textContent = '🔑';
      icon.style.fontSize = '14px';

      const info = document.createElement('div');
      info.style.cssText = 'overflow:hidden;';
      const title = document.createElement('div');
      title.style.cssText = 'font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
      title.textContent = cred.title || cred.username || cred.url;
      const sub = document.createElement('div');
      sub.style.cssText = 'color:#8888aa;font-size:11px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
      sub.textContent = cred.username || '';
      info.appendChild(title);
      info.appendChild(sub);

      row.appendChild(icon);
      row.appendChild(info);
      row.addEventListener('mousedown', (e) => {
        e.preventDefault();
        // Fill scoped to the field the icon belongs to. Clicking the icon on a
        // username field should not also overwrite the password, which is what
        // the unscoped fill used to do.
        fillCredentialScoped(cred, passwordInput);
        removeChip();
      });
      chip.appendChild(row);
    });

    shadow.appendChild(chip);
    document.body.appendChild(host);
    activeChip = host;

    // Close chip on outside click
    setTimeout(function () {
      document.addEventListener('mousedown', onOutsideClick, { once: true, capture: true });
    }, 0);
  }

  function onOutsideClick(e) {
    if (activeChip && !activeChip.contains(e.target)) {
      removeChip();
    }
  }

  function removeChip() {
    if (activeChip) {
      activeChip.remove();
    }
    activeChip = null;
  }


  /**
   * Fills according to which kind of field was clicked.
   *
   * A username or email icon fills only that box, so two-step logins work and a
   * password already present is not clobbered. A password icon fills only the
   * password. Everything else falls back to filling the whole form.
   */
  function fillCredentialScoped(cred, anchorInput) {
    const kind = anchorInput ? classifyInput(anchorInput) : null;

    if (kind === 'username' || kind === 'email') {
      if (cred.username) setNativeValue(anchorInput, cred.username);
      return;
    }

    if (kind === 'password') {
      if (cred.password) setNativeValue(anchorInput, cred.password);
      return;
    }

    // Passkey and anything else: fall back to filling the whole form. A passkey
    // field often doubles as the account box for conditional UI, so putting the
    // username in is the useful behaviour — the WebAuthn ceremony itself has to
    // be started by the page, not by this script.
    fillCredential(cred);
  }

  function fillCredential(cred) {
    const form = document.querySelector('form') || document.body;

    const usernameInput = form.querySelector(
      'input[type="email"], input[type="text"][id*="user"], input[type="text"][name*="user"], input[type="text"][id*="email"], input[type="text"][name*="email"], input[autocomplete="username"], input[autocomplete="email"]'
    ) || form.querySelector('input[type="text"]');

    const passwordInput = form.querySelector('input[type="password"]');

    if (usernameInput && cred.username) {
      setNativeValue(usernameInput, cred.username);
    }
    if (passwordInput && cred.password) {
      setNativeValue(passwordInput, cred.password);
    }
  }

  // Trigger React / Angular / Vue synthetic input events after setting value
  function setNativeValue(el, value) {
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype, 'value'
    )?.set;

    if (nativeInputValueSetter) {
      nativeInputValueSetter.call(el, value);
    } else {
      el.value = value;
    }

    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }


  function handleForm(form) {
    var fields = collectFormFields(form);
    if (!hasPasswordField(fields)) return;

    var url = window.location.href;
    var domain = location.hostname;

    try {
      chrome.runtime.sendMessage({
        type: 'detectForm',
        data: { url: url, fields: fields }
      });
    } catch (err) {
      // best-effort only
    }

    // Icons are attached regardless of whether any credential matches — the icon
    // is how the user asks, and a vault that is locked now may be unlocked in a
    // moment. The chip is no longer popped automatically; it used to appear
    // unbidden over the page on every scan.
    attachIconsWithin(form);

    requestCredentials();
  }

  /// Attaches an icon to every recognised credential field inside a root.
  function attachIconsWithin(root) {
    const scope = root && root.querySelectorAll ? root : document;
    scope.querySelectorAll('input').forEach(attachFieldIcon);
  }

  function watchFormSubmit(form) {
    form.addEventListener('submit', function () {
      var fields = collectFormFields(form);
      if (!hasPasswordField(fields)) return;

      var submission = [];
      for (var i = 0; i < fields.length; i += 1) {
        var f = fields[i];
        var el = document.querySelector(f.selector);
        submission.push({
          selector: f.selector,
          type: f.type,
          id: f.id,
          name: f.name,
          placeholder: f.placeholder,
          autocomplete: f.autocomplete,
          fieldClass: f.fieldClass,
          value: el ? el.value : ''
        });
      }

      try {
        chrome.runtime.sendMessage({
          type: 'submitForm',
          data: { url: window.location.href, fields: submission }
        });
      } catch (err) {
        // ignore
      }
    });
  }


  const processedForms = new WeakSet();

  function scanForms(force = false) {
    document.querySelectorAll('form').forEach(form => {
      if (!force && processedForms.has(form)) return;
      processedForms.add(form);
      handleForm(form);
      watchFormSubmit(form);
    });

    // Plenty of real logins — particularly SPAs — have no <form> element at all.
    // Scanning only forms meant those pages got nothing whatsoever.
    attachIconsWithin(document);

    // Drop icons whose field has since been removed from the DOM.
    fieldIcons.forEach((host, input) => {
      if (!input.isConnected) detachFieldIcon(input);
    });

    repositionAllIcons();
  }

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (!message || !message.type) return false;

    if (message.type === 'triggerFill') {
      const fillMessage = { type: 'fill' };
      chrome.runtime.sendMessage(fillMessage, (resp) => {
        if (resp && resp.success && resp.data && resp.data.hasFill) {
          fillCredential(resp.data);
          sendResponse({ success: true, filled: true });
          return;
        }

        scanForms(true);
        sendResponse({ success: true, filled: false });
      });
      return true;
    }

    return false;
  });

  // Initial scan
  scanForms();

  // Watch for SPA navigation / dynamically injected forms.
  //
  // Coalesced into one pass per animation frame. Busy pages fire subtree
  // mutations continuously, and scanForms now walks every input in the document,
  // so running it per mutation would make the page stutter.
  let scanQueued = false;
  const observer = new MutationObserver(() => {
    if (scanQueued) return;
    scanQueued = true;
    requestAnimationFrame(() => {
      scanQueued = false;
      scanForms();
    });
  });
  observer.observe(document.body, { childList: true, subtree: true });
})();