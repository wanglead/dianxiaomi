(function initializeScanSchema(globalScope) {
  function normalizeOrder(input) {
    const required = ["platform", "storeId", "orderId", "sourceUrl"];
    for (const key of required) {
      if (!String(input?.[key] || "").trim()) {
        throw new Error(`Missing ${key}`);
      }
    }
    if (!input.assessmentAt && !Number.isFinite(input.shippingSeconds)) {
      throw new Error("Missing order time");
    }

    const orderId = String(input.orderId).trim();
    return Object.freeze({
      platform: input.platform,
      storeId: input.storeId,
      orderId,
      orderKey: `${input.platform}:${input.storeId}:${orderId}`,
      sourceUrl: input.sourceUrl,
      assessmentAt: input.assessmentAt || null,
      shippingSeconds: Number.isFinite(input.shippingSeconds)
        ? input.shippingSeconds
        : null,
      rawStatus: String(input.rawStatus || "").trim(),
      rawAssessmentText: String(input.rawAssessmentText || "").trim(),
      rawShippingText: String(input.rawShippingText || "").trim()
    });
  }

  function validateBatch(batch) {
    if (!batch || batch.complete !== true) {
      throw new Error("A complete scan is required");
    }
    if (!Array.isArray(batch.orders)) {
      throw new Error("orders must be an array");
    }
    return batch.orders.map(normalizeOrder);
  }

  const api = { normalizeOrder, validateBatch };
  if (typeof module !== "undefined") {
    module.exports = api;
  }
  globalScope.OrderAlert = Object.assign(globalScope.OrderAlert || {}, api);
})(globalThis);
