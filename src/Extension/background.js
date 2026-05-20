/**
 * PhantomVault Autofill — background service worker / background page.
 *
 * Acts as the bridge between content scripts and the native messaging host
 * (PhantomVault.UI.exe --native-messaging).
 *
 * Native host name: com.phantomvault.autofill
 */

const NATIVE_HOST = 'com.phantomvault.autofill';

let port = null;
let pendingRequests = new Map(); // requestId → { resolve, reject }
let requestCounter = 0;

// ── Port management ────────────────────────────────────────────────────────

function getOrOpenPort() {
  if (port) return port;

  try {
    port = chrome.runtime.connectNative(NATIVE_HOST);

    port.onMessage.addListener((message) => {
      // Messages arriving on the port come back with a requestId so we can
      // route them to the correct pending promise.
      const { _reqId, ...payload } = message;
      const pending = pendingRequests.get(_reqId);
      if (pending) {
        pendingRequests.delete(_reqId);
        pending.resolve(payload);
      }
    });

    port.onDisconnect.addListener(() => {
      const err = chrome.runtime.lastError?.message ?? 'Native host disconnected';
      // Reject all pending requests
      for (const [id, pending] of pendingRequests) {
        pending.reject(new Error(err));
      }
      pendingRequests.clear();
      port = null;
    });
  } catch (e) {
    port = null;
  }

  return port;
}

/**
 * Sends a message to the native host and returns a promise that resolves
 * with the response.
 */
function sendToNativeHost(message) {
  return new Promise((resolve, reject) => {
    const p = getOrOpenPort();
    if (!p) {
      reject(new Error('Could not connect to PhantomVault. Is the app running?'));
      return;
    }

    const reqId = ++requestCounter;
    pendingRequests.set(reqId, { resolve, reject });

    // Attach the request id so we can correlate the response.
    // The native host echoes _reqId back in its response.
    const origin = chrome.runtime.getURL('');
    p.postMessage({ ...message, origin, _reqId: reqId });

    // Timeout after 5 seconds
    setTimeout(() => {
      if (pendingRequests.has(reqId)) {
        pendingRequests.delete(reqId);
        reject(new Error('Native host timed out'));
      }
    }, 5000);
  });
}

// ── Message listener from content scripts ─────────────────────────────────

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || !message.type) return false;

  handleMessage(message, sender)
    .then(sendResponse)
    .catch((err) => sendResponse({ success: false, error: err.message }));

  return true; // keeps the message channel open for async response
});

async function handleMessage(message, sender) {
  switch (message.type) {
    case 'ping': {
      try {
        const resp = await sendToNativeHost({ type: 'ping' });
        return { success: true, connected: true, vaultLocked: !resp?.data?.pong };
      } catch {
        return { success: false, connected: false };
      }
    }

    case 'getVaultState': {
      try {
        // Use a ping to test connectivity; vault lock state comes from the native host response.
        const resp = await sendToNativeHost({ type: 'ping' });
        return { success: true, pong: resp?.data?.pong ?? false };
      } catch (err) {
        return { success: false, error: err.message };
      }
    }

    case 'detectForm': {
      try {
        const resp = await sendToNativeHost({ type: 'detectForm', data: message.data });
        return resp;
      } catch (err) {
        return { success: false, error: err.message };
      }
    }

    case 'getCredentials': {
      try {
        const resp = await sendToNativeHost({ type: 'getCredentials', data: message.data });
        return resp;
      } catch (err) {
        return { success: false, error: err.message };
      }
    }

    case 'submitForm': {
      try {
        const resp = await sendToNativeHost({ type: 'submitForm', data: message.data });
        return resp;
      } catch (err) {
        return { success: false, error: err.message };
      }
    }

    default:
      return { success: false, error: `Unknown message type: ${message.type}` };
  }
}
