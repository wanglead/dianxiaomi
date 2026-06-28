(function initializeOrderAlertContent(globalScope) {
  const api = globalScope.OrderAlert;

  function selectAdapter(location, document) {
    return [
      api.DianxiaomiOrderAdapter,
      api.AliExpressOrderAdapter
    ].find(adapter => adapter?.matches(location, document));
  }

  async function runCurrentPlatformScan() {
    const adapter = selectAdapter(globalScope.location, globalScope.document);
    if (!adapter) throw new Error("The current order page is not recognized");
    const boundAdapter = {
      extractCurrentPage: () => adapter.extractCurrentPage(
        globalScope.document,
        globalScope.location
      )
    };
    return api.scanAllPages(
      boundAdapter,
      api.createDomPager(globalScope.document)
    );
  }

  if (globalScope.chrome?.runtime?.onMessage) {
    globalScope.chrome.runtime.onMessage.addListener(
      (message, _sender, sendResponse) => {
        if (message?.type !== "order-alert:scan") return false;
        runCurrentPlatformScan(message.account).then(
          batch => sendResponse({ ok: true, batch }),
          error => sendResponse({
            ok: false,
            error: { code: "SCAN_FAILED", message: error.message }
          })
        );
        return true;
      }
    );
  }

  api.runCurrentPlatformScan = runCurrentPlatformScan;
})(globalThis);
