(function initializeScanCoordinator(globalScope) {
  const PAGE_LIMIT = 100;

  async function scanAllPages(adapter, pager, options = {}) {
    const maxPages = options.maxPages || PAGE_LIMIT;
    const orders = new Map();
    const fingerprints = new Set();
    let pageCount = 0;

    while (true) {
      if (pageCount >= maxPages) {
        throw new Error(`Scan exceeded the ${maxPages} page limit`);
      }

      await pager.waitUntilReady?.();
      const fingerprint = String(await pager.fingerprint());
      if (fingerprints.has(fingerprint)) {
        throw new Error(`Scan reached a repeated page fingerprint on page ${pageCount + 1}`);
      }
      fingerprints.add(fingerprint);
      pageCount += 1;

      let pageOrders;
      try {
        pageOrders = await adapter.extractCurrentPage();
      } catch (error) {
        throw new Error(`Failed to scan page ${pageCount}: ${error.message}`, {
          cause: error
        });
      }
      if (!Array.isArray(pageOrders)) {
        throw new Error(`Failed to scan page ${pageCount}: adapter returned invalid orders`);
      }
      for (const order of pageOrders) {
        if (!order?.orderKey) {
          throw new Error(`Failed to scan page ${pageCount}: order key is missing`);
        }
        orders.set(order.orderKey, order);
      }

      if (!await pager.hasNext()) break;
      await pager.next();
    }

    return {
      complete: true,
      scannedAt: new Date().toISOString(),
      pageCount,
      orders: Array.from(orders.values())
    };
  }

  function createDomPager(document, options = {}) {
    const timeoutMs = options.timeoutMs || 15000;
    const nextSelectors = [
      "[data-next-page]:not([disabled])",
      ".pagination-next:not(.disabled)",
      "button[aria-label='Next']:not([disabled])"
    ];

    function orderContainer() {
      return document.querySelector("[data-order-list], .order-list");
    }

    function fingerprint() {
      const rows = Array.from(document.querySelectorAll(
        "[data-order-row], .order-list .order-item, table.order-list tbody tr"
      ));
      return rows
        .slice(0, 5)
        .map(row => row.querySelector("[data-order-id], .order-id")?.textContent?.trim() || "")
        .join("|");
    }

    async function waitFor(predicate, errorMessage) {
      const startedAt = Date.now();
      while (!predicate()) {
        if (Date.now() - startedAt >= timeoutMs) throw new Error(errorMessage);
        await new Promise(resolve => setTimeout(resolve, 100));
      }
    }

    return {
      fingerprint,
      async waitUntilReady() {
        await waitFor(() => Boolean(orderContainer()), "Order list did not become ready");
        await waitFor(
          () => !document.querySelector("[data-loading='true'], .loading-mask:not([hidden])"),
          "Order list did not finish loading"
        );
      },
      hasNext() {
        return nextSelectors.some(selector => document.querySelector(selector));
      },
      async next() {
        const before = fingerprint();
        const control = nextSelectors
          .map(selector => document.querySelector(selector))
          .find(Boolean);
        if (!control) throw new Error("Next page control is unavailable");
        control.click();
        await waitFor(
          () => fingerprint() !== before,
          "Next page did not replace the current rows"
        );
      }
    };
  }

  const api = { scanAllPages, createDomPager };
  if (typeof module !== "undefined") module.exports = api;
  globalScope.OrderAlert = Object.assign(globalScope.OrderAlert || {}, api);
})(globalThis);
