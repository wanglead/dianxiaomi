const test = require("node:test");
const assert = require("node:assert/strict");
const { normalizeOrder, validateBatch } = require("../order-alert/scan-schema.js");

test("normalizes an order key without crossing stores", () => {
  const order = normalizeOrder({
    platform: "aliexpress",
    storeId: "us-store",
    orderId: "815209",
    sourceUrl: "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176",
    assessmentAt: null,
    shippingSeconds: 3600,
    rawStatus: "Awaiting shipment"
  });

  assert.equal(order.orderKey, "aliexpress:us-store:815209");
});

test("rejects an incomplete batch", () => {
  assert.throws(
    () => validateBatch({ complete: false, orders: [] }),
    /complete scan/
  );
});
