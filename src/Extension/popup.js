document.addEventListener('DOMContentLoaded', () => {
  const dot = document.getElementById('status-dot');
  const statusText = document.getElementById('status-text');
  const btnFill = document.getElementById('btn-fill');

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
    // Ask the content script on the active tab to trigger credential fill
    chrome.tabs.query({ active: true, currentWindow: true }, ([tab]) => {
      if (!tab?.id) return;
      chrome.tabs.sendMessage(tab.id, { type: 'triggerFill' });
      window.close();
    });
  });
});
