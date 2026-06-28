const test = require("node:test");
const assert = require("node:assert/strict");
const { createNativeBridge } = require("../order-alert/native-bridge.js");

function event() {
  const listeners = [];
  return {
    addListener(listener) { listeners.push(listener); },
    async emit(value) {
      await Promise.all(listeners.map(listener => listener(value)));
    }
  };
}

function fakeChrome() {
  const port = {
    onMessage: event(),
    onDisconnect: event(),
    posted: [],
    postMessage(message) { this.posted.push(message); }
  };
  const storageChanges = [];
  const sent = [];
  return {
    port,
    storageChanges,
    sent,
    api: {
      runtime: { connectNative: () => port },
      storage: {
        local: {
          async set(value) { storageChanges.push(value); }
        }
      },
      tabs: {
        async query() { return [{ id: 7 }]; },
        async create() { throw new Error("should not create a tab"); },
        async sendMessage(tabId, message) {
          sent.push({ tabId, message });
          return { ok: true, batch: { complete: true, orders: [] } };
        }
      }
    }
  };
}

test("forwards scanAccount to the target tab and preserves request ID", async () => {
  const chrome = fakeChrome();
  createNativeBridge(chrome.api);

  await chrome.port.onMessage.emit({
    requestId: "request-1",
    type: "scanAccount",
    payload: { url: "https://www.dianxiaomi.com/web/order/paid?go=m100" }
  });

  assert.equal(chrome.sent[0].tabId, 7);
  assert.equal(chrome.sent[0].message.type, "order-alert:scan");
  assert.equal(chrome.port.posted[0].requestId, "request-1");
  assert.equal(chrome.port.posted[0].type, "scanResult");
});

test("returns a structured error for unsupported commands", async () => {
  const chrome = fakeChrome();
  createNativeBridge(chrome.api);

  await chrome.port.onMessage.emit({ requestId: "request-2", type: "mystery" });

  assert.equal(chrome.port.posted[0].requestId, "request-2");
  assert.equal(chrome.port.posted[0].error.code, "UNSUPPORTED_COMMAND");
});

test("marks the native host offline after disconnect", async () => {
  const chrome = fakeChrome();
  createNativeBridge(chrome.api);

  await chrome.port.onDisconnect.emit();

  assert.deepEqual(chrome.storageChanges.at(-1), { orderAlertHostOnline: false });
});
