document.addEventListener('DOMContentLoaded', () => {
  const dot = document.getElementById('status-dot');
  const statusText = document.getElementById('status-text');
  const btnFill = document.getElementById('btn-fill');
  const btnSiteAccess = document.getElementById('btn-site-access');
  const siteAccessText = document.getElementById('site-access-text');
  let activeTab = null;
  let activeOriginPattern = null;
  let hasPersistentAccess = false;

  function resolveOriginPattern(urlValue) {
    try {
      const url = new URL(urlValue);
      if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
      return `${url.origin}/*`;
    } catch {
      return null;
    }
  }

  function refreshSiteAccess() {
    if (!activeOriginPattern) {
      siteAccessText.textContent = 'Site access unavailable on this page';
      btnSiteAccess.disabled = true;
      return;
    }

    chrome.permissions.contains({ origins: [activeOriginPattern] }, (granted) => {
      hasPersistentAccess = Boolean(granted) && !chrome.runtime.lastError;
      siteAccessText.textContent = hasPersistentAccess
        ? 'Site access: always allowed'
        : 'Site access: one-time only';
      btnSiteAccess.textContent = hasPersistentAccess
        ? 'Remove access for this site'
        : 'Always allow on this site';
      btnSiteAccess.disabled = false;
    });
  }

  // Apply the cached, non-secret theme/UI prefs (mirrored from the desktop app).
  try {
    chrome.storage.local.get('phantomSyncState', (items) => {
      const sync = items && items.phantomSyncState;
      if (sync && sync.themeId) {
        document.documentElement.setAttribute('data-theme', sync.themeId);
      }
    });
  } catch {
    /* storage unavailable; popup still works without theming */
  }

  function setStatus(state, text) {
    dot.className = `dot ${state}`;
    statusText.textContent = text;
    btnFill.disabled = state !== 'connected';
  }

  // Ping the native host via the background script
  chrome.runtime.sendMessage({ type: 'ping' })
    .then(resp => {
      if (!resp || !resp.connected) {
        setStatus('disconnected', 'PhantomVault not running');
      } else if (resp.vaultLocked) {
        setStatus('locked', 'Vault locked — unlock the app');
      } else {
        setStatus('connected', 'Connected & vault unlocked');
      }
    })
    .catch(() => setStatus('disconnected', 'PhantomVault not running'));

  btnFill.addEventListener('click', () => {
    if (!activeTab?.id || !activeOriginPattern) return;

    // activeTab is granted by this direct user gesture. Inject only now; no
    // page receives the content script merely because the extension is installed.
    chrome.runtime.sendMessage({ type: 'injectActiveTab', tabId: activeTab.id })
      .then(() => chrome.tabs.sendMessage(activeTab.id, { type: 'triggerFill' }))
      .then(() => window.close())
      .catch(() => {
        siteAccessText.textContent = 'This browser page does not allow extensions';
      });
  });

  btnSiteAccess.addEventListener('click', () => {
    if (!activeOriginPattern) return;
    const operation = hasPersistentAccess ? chrome.permissions.remove : chrome.permissions.request;
    operation.call(chrome.permissions, { origins: [activeOriginPattern] }, (changed) => {
      if (chrome.runtime.lastError || !changed) {
        siteAccessText.textContent = 'Site access was not changed';
        return;
      }
      refreshSiteAccess();
    });
  });

  chrome.tabs.query({ active: true, currentWindow: true }, ([tab]) => {
    activeTab = tab ?? null;
    activeOriginPattern = resolveOriginPattern(tab?.url);
    refreshSiteAccess();
  });
});
