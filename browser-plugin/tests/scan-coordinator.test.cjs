const test = require("node:test");
const assert = require("node:assert/strict");
const { scanAllPages } = require("../order-alert/scan-coordinator.js");

function order(key) {
  return { orderKey: key };
}

function pager(fingerprints) {
  let index = 0;
  return {
    fingerprint: () => fingerprints[index],
    hasNext: () => index < fingerprints.length - 1,
    next: async () => { index += 1; }
  };
}

test("deduplicates an order seen on two pages", async () => {
  const pages = [[order("dianxiaomi:main:1")], [order("dianxiaomi:main:1")]];
  let index = 0;
  const result = await scanAllPages(
    { extractCurrentPage: () => pages[index++] },
    pager(["page-1", "page-2"])
  );

  assert.equal(result.complete, true);
  assert.equal(result.pageCount, 2);
  assert.equal(result.orders.length, 1);
});

test("rejects when page two fails", async () => {
  let page = 0;
  await assert.rejects(
    () => scanAllPages({
      extractCurrentPage() {
        page += 1;
        if (page === 2) throw new Error("broken DOM");
        return [order(`dianxiaomi:main:${page}`)];
      }
    }, pager(["page-1", "page-2", "page-3"])),
    /page 2/
  );
});

test("rejects repeated page fingerprints", async () => {
  await assert.rejects(
    () => scanAllPages(
      { extractCurrentPage: () => [order("dianxiaomi:main:1")] },
      pager(["same", "same"])
    ),
    /repeated page/
  );
});

test("enforces the page limit", async () => {
  const fingerprints = Array.from({ length: 101 }, (_, i) => `page-${i}`);
  await assert.rejects(
    () => scanAllPages(
      { extractCurrentPage: () => [order("dianxiaomi:main:1")] },
      pager(fingerprints)
    ),
    /100 page/
  );
});
