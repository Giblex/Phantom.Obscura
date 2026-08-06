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
        fillCredential(cred);
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

    chrome.runtime.sendMessage({
      type: 'getCredentials',
      data: { domain: domain }
    }, function (resp) {
      if (!resp || !resp.success || !resp.data || !Array.isArray(resp.data.credentials) || resp.data.credentials.length === 0) {
        return;
      }

      var passwordInput = form.querySelector('input[type="password"]');
      if (passwordInput) {
        showSuggestionChip(passwordInput, resp.data.credentials);
        passwordInput.addEventListener('focus', function () {
          showSuggestionChip(passwordInput, resp.data.credentials);
        }, { once: true });
      }
    });
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

  // Watch for SPA navigation / dynamically injected forms
  const observer = new MutationObserver(() => scanForms());
  observer.observe(document.body, { childList: true, subtree: true });
})();