(function initializeNativeBridge(globalScope) {
  const HOST_NAME = "com.orderalert.native_host";

  async function findOrCreateTab(chromeApi, url) {
    const existing = await chromeApi.tabs.query({ url });
    if (existing.length > 0) return existing[0];
    return chromeApi.tabs.create({ url, active: false });
  }

  async function waitForTabReady(chromeApi, tab) {
    if (tab.status === "complete" || typeof chromeApi.tabs.get !== "function") {
      return tab;
    }
    const startedAt = Date.now();
    while (Date.now() - startedAt < 30000) {
      const current = await chromeApi.tabs.get(tab.id);
      if (current.status === "complete") return current;
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error("The order page did not finish loading");
  }

  async function routeCommand(chromeApi, port, command) {
    const requestId = command?.requestId || null;
    if (command?.type !== "scanAccount") {
      port.postMessage({
        requestId,
        type: "error",
        error: {
          code: "UNSUPPORTED_COMMAND",
          message: `Unsupported command: ${command?.type || "unknown"}`
        }
      });
      return;
    }

    try {
      const targetUrl = command.payload?.url;
      if (!targetUrl) throw new Error("A target URL is required");
      const tab = await waitForTabReady(
        chromeApi,
        await findOrCreateTab(chromeApi, targetUrl)
      );
      const result = await chromeApi.tabs.sendMessage(tab.id, {
        type: "order-alert:scan",
        account: command.payload?.account || null
      });
      port.postMessage({
        requestId,
        type: "scanResult",
        payload: result
      });
    } catch (error) {
      port.postMessage({
        requestId,
        type: "error",
        error: { code: "SCAN_COMMAND_FAILED", message: error.message }
      });
    }
  }

  function createNativeBridge(chromeApi) {
    const port = chromeApi.runtime.connectNative(HOST_NAME);
    port.onMessage.addListener(
      command => routeCommand(chromeApi, port, command)
    );
    port.onDisconnect.addListener(
      () => chromeApi.storage.local.set({ orderAlertHostOnline: false })
    );
    chromeApi.storage.local.set({ orderAlertHostOnline: true });
    return port;
  }

  const api = { HOST_NAME, createNativeBridge, routeCommand };
  if (typeof module !== "undefined") module.exports = api;
  globalScope.OrderAlert = Object.assign(globalScope.OrderAlert || {}, api);
})(globalThis);
