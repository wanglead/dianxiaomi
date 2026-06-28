(function initializeDomUtils(globalScope) {
  const text = (root, selectors) => selectors
    .map(selector => root.querySelector(selector)?.textContent?.trim())
    .find(Boolean) || "";

  function parseCountdown(value) {
    const source = String(value || "").trim();
    const clock = source.match(/^(\d+):(\d{2}):(\d{2})$/);
    if (clock) {
      return Number(clock[1]) * 3600 + Number(clock[2]) * 60 + Number(clock[3]);
    }
    const days = Number(source.match(/(\d+)\s*天/)?.[1] || 0);
    const hours = Number(source.match(/(\d+)\s*(?:小时|时)/)?.[1] || 0);
    const minutes = Number(source.match(/(\d+)\s*分/)?.[1] || 0);
    return days || hours || minutes
      ? days * 86400 + hours * 3600 + minutes * 60
      : null;
  }

  function parseLocalDateTime(value) {
    const source = String(value || "").trim();
    const match = source.match(
      /^(\d{4})[-/](\d{2})[-/](\d{2})\s+(\d{2}):(\d{2})(?::(\d{2}))?$/
    );
    if (!match) return null;
    const [, year, month, day, hour, minute, second = "00"] = match;
    return `${year}-${month}-${day}T${hour}:${minute}:${second}+08:00`;
  }

  function findOrderRows(document) {
    return Array.from(document.querySelectorAll(
      "[data-order-list] [data-order-row], table.order-list tbody tr, .order-list .order-item"
    ));
  }

  function detectStoreId(document, fallback = "") {
    return document.querySelector("[data-order-list]")?.dataset?.storeId
      || document.querySelector("[data-store-id]")?.dataset?.storeId
      || fallback;
  }

  const api = {
    text,
    parseCountdown,
    parseLocalDateTime,
    findOrderRows,
    detectStoreId
  };
  if (typeof module !== "undefined") module.exports = api;
  globalScope.OrderAlert = Object.assign(globalScope.OrderAlert || {}, api);
})(globalThis);
