/**
 * PhantomVault Autofill — content script.
 *
 * Detects login/registration forms, sends field metadata to the background
 * script (which relays to the native host), and injects a suggestion chip
 * that fills credentials when clicked.
 */

(function () {
  'use strict';

  // Prevent double-injection in the same frame
  if (window.__phantomVaultInjected) return;
  window.__phantomVaultInjected = true;

  // ── Field / form detection ───────────────────────────────────────────────

  const PASSWORD_HINTS = ['password', 'passwd', 'pass', 'pwd'];
  const USERNAME_HINTS = ['username', 'user', 'login', 'account', 'email', 'e-mail', 'mail'];
  const TOTP_HINTS = ['2fa', 'mfa', 'otp', 'token', 'code', 'verification', 'auth-code', 'totp'];

  function classifyInput(el) {
    const attrs = [el.type, el.id, el.name, el.placeholder, el.autocomplete]
      .map(v => (v || '').toLowerCase());
    const combined = attrs.join(' ');

    if (el.type === 'password') return 'password';
    if (TOTP_HINTS.some(h => combined.includes(h))) return 'totp';
    if (USERNAME_HINTS.some(h => combined.includes(h))) return 'username';
    if (el.type === 'email') return 'email';
    return null;
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

  // ── Suggestion chip UI ──────────────────────────────────────────────────

  let activeChip = null;

  function showSuggestionChip(passwordInput, credentials) {
    removeChip();

    if (!credentials || credentials.length === 0) return;

    const rect = passwordInput.getBoundingClientRect();
    const chip = document.createElement('div');
    chip.id = '__phantomvault_chip__';
    chip.style.cssText = `
      position: fixed;
      z-index: 2147483647;
      top: ${rect.bottom + window.scrollY + 4}px;
      left: ${rect.left + window.scrollX}px;
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

    credentials.slice(0, 5).forEach(cred => {
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
        fillCredential(cred);
        removeChip();
      });
      chip.appendChild(row);
    });

    document.body.appendChild(chip);
    activeChip = chip;

    // Close chip on outside click
    setTimeout(() => {
      document.addEventListener('mousedown', onOutsideClick, { once: true, capture: true });
    }, 0);
  }

  function onOutsideClick(e) {
    if (activeChip && !activeChip.contains(e.target)) {
      removeChip();
    }
  }

  function removeChip() {
    activeChip?.remove();
    activeChip = null;
  }

  // ── Credential fill ──────────────────────────────────────────────────────

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

  // ── Form detection & credential request ─────────────────────────────────

  async function handleForm(form) {
    const fields = collectFormFields(form);
    if (!hasPasswordField(fields)) return;

    const domain = location.hostname;

    // Notify native host of form detection
    chrome.runtime.sendMessage({
      type: 'detectForm',
      data: { domain, fields }
    }).catch(() => {});

    // Request matching credentials
    let resp;
    try {
      resp = await chrome.runtime.sendMessage({
        type: 'getCredentials',
        data: { domain }
      });
    } catch {
      return;
    }

    if (!resp?.success || !resp.data?.credentials?.length) return;

    const passwordInput = form.querySelector('input[type="password"]');
    if (passwordInput) {
      showSuggestionChip(passwordInput, resp.data.credentials);
      passwordInput.addEventListener('focus', () => {
        showSuggestionChip(passwordInput, resp.data.credentials);
      }, { once: true });
    }
  }

  // ── Submit capture ───────────────────────────────────────────────────────

  function watchFormSubmit(form) {
    form.addEventListener('submit', () => {
      const fields = collectFormFields(form);
      if (!hasPasswordField(fields)) return;

      const data = {};
      fields.forEach(f => {
        const el = document.querySelector(f.selector);
        if (el) data[f.fieldClass] = el.value;
      });

      chrome.runtime.sendMessage({
        type: 'submitForm',
        data: { domain: location.hostname, fields: data }
      }).catch(() => {});
    });
  }

  // ── DOM scanning ─────────────────────────────────────────────────────────

  const processedForms = new WeakSet();

  function scanForms() {
    document.querySelectorAll('form').forEach(form => {
      if (processedForms.has(form)) return;
      processedForms.add(form);
      handleForm(form);
      watchFormSubmit(form);
    });
  }

  // Initial scan
  scanForms();

  // Watch for SPA navigation / dynamically injected forms
  const observer = new MutationObserver(() => scanForms());
  observer.observe(document.body, { childList: true, subtree: true });
})();
