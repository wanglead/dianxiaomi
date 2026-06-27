const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { JSDOM } = require("jsdom");
const { normalizeOrder, validateBatch } = require("../order-alert/scan-schema.js");
const dianxiaomi = require("../order-alert/dianxiaomi-adapter.js");
const aliexpress = require("../order-alert/aliexpress-adapter.js");

function fixture(name, url) {
  const html = fs.readFileSync(path.join(__dirname, "fixtures", name), "utf8");
  return new JSDOM(html, { url });
}

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

test("extracts a normalized Dianxiaomi order", () => {
  const dom = fixture(
    "dianxiaomi-orders.html",
    "https://www.dianxiaomi.com/web/order/paid?go=m100"
  );

  const orders = dianxiaomi.extractCurrentPage(
    dom.window.document,
    dom.window.location
  );

  assert.deepEqual(orders, [{
    platform: "dianxiaomi",
    storeId: "dxm-main",
    orderId: "DXM372",
    orderKey: "dianxiaomi:dxm-main:DXM372",
    sourceUrl: "https://www.dianxiaomi.com/web/order/paid?go=m100",
    assessmentAt: "2026-06-24T18:30:00+08:00",
    shippingSeconds: 66300,
    rawStatus: "已付款",
    rawAssessmentText: "2026-06-24 18:30",
    rawShippingText: "18小时25分"
  }]);
});

test("extracts a normalized AliExpress order", () => {
  const dom = fixture(
    "aliexpress-orders.html",
    "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176"
  );

  const orders = aliexpress.extractCurrentPage(
    dom.window.document,
    dom.window.location
  );

  assert.equal(orders[0].storeId, "ali-us");
  assert.equal(orders[0].orderId, "815209");
  assert.equal(orders[0].shippingSeconds, 6136);
  assert.equal(orders[0].rawStatus, "Awaiting shipment");
});

test("rejects malformed time text without guessing", () => {
  const dom = new JSDOM(`
    <div data-order-list data-store-id="ali-us">
      <article data-order-row>
        <span data-order-id>bad-time</span>
        <time data-shipping-time>later maybe</time>
      </article>
    </div>
  `, { url: "https://csp.aliexpress.com/m_apps/order-manage/orderList" });

  assert.throws(
    () => aliexpress.extractCurrentPage(dom.window.document, dom.window.location),
    /Missing order time/
  );
});
