(function initializeDianxiaomiAdapter(globalScope) {
  const schema = typeof require === "function"
    ? require("./scan-schema.js")
    : globalScope.OrderAlert;
  const utils = typeof require === "function"
    ? require("./dom-utils.js")
    : globalScope.OrderAlert;

  function matches(location, document) {
    return location.hostname === "www.dianxiaomi.com"
      && location.pathname.startsWith("/web/order/")
      && Boolean(document.querySelector("[data-order-list], .order-list"));
  }

  function getStoreId(document) {
    return utils.detectStoreId(document, "dianxiaomi-main");
  }

  function extractCurrentPage(document, location) {
    const storeId = getStoreId(document);
    return utils.findOrderRows(document).map(row => {
      const rawAssessmentText = utils.text(row, [
        "[data-assessment-time]",
        ".assessment-time"
      ]);
      const rawShippingText = utils.text(row, [
        "[data-shipping-time]",
        ".shipping-time"
      ]);
      return schema.normalizeOrder({
        platform: "dianxiaomi",
        storeId,
        orderId: utils.text(row, ["[data-order-id]", ".order-id"]),
        sourceUrl: location.href,
        assessmentAt: utils.parseLocalDateTime(rawAssessmentText),
        shippingSeconds: utils.parseCountdown(rawShippingText),
        rawStatus: utils.text(row, ["[data-status]", ".order-status"]),
        rawAssessmentText,
        rawShippingText
      });
    });
  }

  const api = { matches, getStoreId, extractCurrentPage };
  if (typeof module !== "undefined") module.exports = api;
  globalScope.OrderAlert = Object.assign(globalScope.OrderAlert || {}, {
    DianxiaomiOrderAdapter: api
  });
})(globalThis);
