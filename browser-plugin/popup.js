// 商品采集助手 - Popup Script
document.addEventListener("DOMContentLoaded", async () => {
  const statusEl = document.getElementById("status");
  const currentProductEl = document.getElementById("currentProduct");
  const noProductEl = document.getElementById("noProduct");
  const btnExtract = document.getElementById("btnExtract");
  const btnImport = document.getElementById("btnImport");
  const btnOpenSystem = document.getElementById("btnOpenSystem");

  let currentProduct = null

  async function refreshLocalServiceStatus() {
    try {
      const response = await fetch("http://localhost:8000/api/plugin/dianxiaomi/status");
      const data = await response.json();
      statusEl.textContent = `本地服务运行中，待同步 ${data.pending_count || 0} 个`;
      statusEl.className = "status success";
    } catch (error) {
      statusEl.textContent = "本地服务未连接";
      statusEl.className = "status error";
    }
  }

  refreshLocalServiceStatus();

  // 检查当前页面是否有商品信息
  try {
    const result = await chrome.storage.local.get("currentProduct");
    if (result.currentProduct && result.currentProduct.title) {
      currentProduct = result.currentProduct;
      showProduct(currentProduct);
    }
  } catch (e) {
    console.log("No stored product");
  }

  function showProduct(product) {
    currentProductEl.style.display = "block";
    noProductEl.style.display = "none";
    document.getElementById("productTitle").textContent = product.title || "未命名";
    document.getElementById("productPlatform").textContent = product.platform || "未知";
    document.getElementById("productPrice").textContent = product.price ? `¥${product.price}` : "";
  }

  // 采集当前商品
  btnExtract.addEventListener("click", async () => {
    statusEl.textContent = "正在采集...";
    statusEl.className = "status";

    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      const result = await chrome.tabs.sendMessage(tab.id, { action: "extractProduct" });

      if (result && result.success) {
        currentProduct = result.data;
        await chrome.storage.local.set({ currentProduct: currentProduct });
        showProduct(currentProduct);
        statusEl.textContent = "采集成功！";
        statusEl.className = "status success";
      } else {
        statusEl.textContent = "采集失败：未检测到商品信息";
        statusEl.className = "status error";
      }
    } catch (e) {
      statusEl.textContent = "采集失败：页面不兼容，请刷新后重试";
      statusEl.className = "status error";
    }
  });

  // 导入到系统
  btnImport.addEventListener("click", async () => {
    if (!currentProduct) {
      statusEl.textContent = "请先采集商品";
      statusEl.className = "status error";
      return;
    }

    statusEl.textContent = "正在导入...";
    statusEl.className = "status";

    try {
      const resp = await fetch("http://localhost:8000/api/products/collect/by-plugin", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          token: "plugin-token-change-in-production",
          products: [{
            title: currentProduct.title,
            description: currentProduct.description,
            original_price: currentProduct.price,
            images: currentProduct.images,
            source_url: currentProduct.url,
            source_platform: currentProduct.platform,
            variants: currentProduct.variants || [],
            category: currentProduct.category || "",
          }]
        })
      });

      const data = await resp.json();
      if (data.success) {
        statusEl.textContent = `导入成功！已导入 ${data.imported_count} 个商品`;
        statusEl.className = "status success";
      } else {
        statusEl.textContent = `导入失败：${data.errors?.join(", ") || "未知错误"}`;
        statusEl.className = "status error";
      }
    } catch (e) {
      statusEl.textContent = "导入失败：无法连接到服务器 (localhost:8000)";
      statusEl.className = "status error";
    }
  });

  // 打开管理系统
  btnOpenSystem.addEventListener("click", () => {
    chrome.tabs.create({ url: "http://localhost:8000" });
  });
});
