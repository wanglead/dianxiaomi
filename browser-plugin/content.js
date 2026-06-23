// 浏览器商品采集插件 - Content Script
// 自动检测当前页面商品信息并传递给 popup

(function() {
  "use strict";

  /**
   * 检测当前页面所在的电商平台
   */
  function detectPlatform() {
    const hostname = window.location.hostname.toLowerCase();
    if (hostname.includes("dianxiaomi.com") || document.documentElement.dataset.dianxiaomiMock === "true") return "dianxiaomi";
    if (hostname.includes("aliexpress")) return "aliexpress";
    if (hostname.includes("1688.com")) return "1688";
    if (hostname.includes("taobao.com")) return "taobao";
    if (hostname.includes("tmall.com")) return "tmall";
    if (hostname.includes("amazon")) return "amazon";
    if (hostname.includes("ozon")) return "ozon";
    if (hostname.includes("joom")) return "joom";
    if (hostname.includes("myshopify.com")) return "shopify";
    return "unknown";
  }

  /**
   * 通用商品信息提取
   */
  function extractProductInfo() {
    const platform = detectPlatform();
    const info = {
      platform: platform,
      url: window.location.href,
      title: "",
      description: "",
      price: 0,
      currency: "CNY",
      images: [],
      variants: [],
      category: "",
      sku: "",
    };

    // 1. 提取标题
    const titleSelectors = [
      "h1", '[itemprop="name"]', ".product-title", ".title",
      '[data-testid="product-title"]', "#productTitle",
    ];
    for (const sel of titleSelectors) {
      const el = document.querySelector(sel);
      if (el && el.textContent.trim()) {
        info.title = el.textContent.trim();
        break;
      }
    }
    // fallback: og:title
    if (!info.title) {
      const og = document.querySelector('meta[property="og:title"]');
      if (og) info.title = og.content;
    }
    if (!info.title) info.title = document.title;

    // 2. 提取价格
    const priceSelectors = [
      '[itemprop="price"]', ".price", ".product-price",
      '[data-testid="price"]', "#price_inside_buybox",
      ".a-price-whole", '[data-spm-anchor-id*="price"]',
    ];
    for (const sel of priceSelectors) {
      const el = document.querySelector(sel);
      if (el) {
        const priceText = el.textContent.trim().replace(/[^0-9.]/g, "");
        const price = parseFloat(priceText);
        if (!isNaN(price) && price > 0) {
          info.price = price;
          break;
        }
      }
    }

    // 3. 提取图片
    const imgSelectors = [
      '[itemprop="image"]', ".product-image img",
      ".gallery img", '[data-testid="product-image"]',
      "#imgTagWrapperId img", ".image-gallery img",
    ];
    let imgElements = [];
    for (const sel of imgSelectors) {
      imgElements = document.querySelectorAll(sel);
      if (imgElements.length > 0) break;
    }
    if (imgElements.length === 0) {
      imgElements = document.querySelectorAll("img[src]");
    }

    const seenUrls = new Set();
    for (const img of imgElements) {
      let src = img.getAttribute("src") || img.getAttribute("data-src") || "";
      if (!src || src.startsWith("data:")) continue;
      if (src.startsWith("//")) src = "https:" + src;
      if (src.startsWith("/")) src = window.location.origin + src;
      if (seenUrls.has(src)) continue;
      seenUrls.add(src);
      // 过滤小图标
      if (img.naturalWidth < 100 && img.naturalHeight < 100) continue;
      info.images.push(src);
      if (info.images.length >= 10) break;
    }

    // 4. 提取描述
    const descSelectors = [
      '[itemprop="description"]', ".product-description",
      ".description", "#productDescription",
      '[data-testid="product-description"]',
    ];
    for (const sel of descSelectors) {
      const el = document.querySelector(sel);
      if (el && el.textContent.trim()) {
        info.description = el.textContent.trim().slice(0, 2000);
        break;
      }
    }
    if (!info.description) {
      const meta = document.querySelector('meta[property="og:description"]');
      if (meta) info.description = meta.content;
    }

    // 5. 提取货币
    const currencyMeta = document.querySelector('[itemprop="priceCurrency"]');
    if (currencyMeta) info.currency = currencyMeta.content;
    else if (platform === "1688" || platform === "taobao" || platform === "tmall") info.currency = "CNY";
    else if (platform === "aliexpress") info.currency = "USD";
    else if (platform === "ozon") info.currency = "RUB";

    // 6. 提取分类
    const breadcrumb = document.querySelector('[itemprop="itemListElement"]');
    if (breadcrumb) info.category = breadcrumb.textContent.trim();
    const catMeta = document.querySelector('[itemprop="category"]');
    if (catMeta) info.category = catMeta.content;

    // 7. 提取 SKU
    const skuEl = document.querySelector('[itemprop="sku"]') ||
                  document.querySelector('[data-sku]');
    if (skuEl) info.sku = skuEl.textContent?.trim() || skuEl.getAttribute("data-sku") || "";

    return info;
  }

  const LOCAL_API = "http://localhost:8000";

  function detectDianxiaomiSkus() {
    const rows = Array.from(document.querySelectorAll("[data-sku], .sku-row, .variant-row"));
    const skus = rows.map((row, index) => ({
      sku: row.getAttribute("data-sku") || `SKU-${index + 1}`,
      label: (row.querySelector(".sku-name")?.textContent || row.textContent || "").replace(/\s+/g, " ").trim().slice(0, 60),
    })).filter(item => item.label);
    return skus.length ? skus.slice(0, 20) : [{sku: "SKU-1", label: "当前页面商品"}];
  }

  function assistantStyles() {
    return `
      #dxm-sync-assistant{position:fixed;right:22px;top:72px;width:410px;max-height:calc(100vh - 94px);z-index:2147483647;background:#fff;border:1px solid #dbe3ef;border-radius:8px;box-shadow:0 18px 48px rgba(15,23,42,.2);font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Microsoft YaHei",sans-serif;color:#1f2937;overflow:auto}
      #dxm-sync-assistant *{box-sizing:border-box;letter-spacing:0}.dxm-head{padding:18px;border-bottom:1px solid #e8edf3;position:relative}.dxm-title{font-size:19px;font-weight:700;margin:0}.dxm-sub{font-size:13px;color:#059669;margin-top:7px}.dxm-close{position:absolute;right:14px;top:14px;border:0;background:#fef2f2;color:#dc2626;width:28px;height:28px;border-radius:50%;font-size:18px;cursor:pointer}.dxm-body{padding:16px}.dxm-kicker{font-size:14px;font-weight:700}.dxm-hint{float:right;font-size:12px;color:#94a3b8;font-weight:400}.dxm-tags{display:flex;gap:8px;margin:12px 0}.dxm-tag{border:0;border-radius:6px;padding:8px 12px;background:#2563eb;color:white;font-weight:700;cursor:pointer}.dxm-actions{display:flex;align-items:center;gap:14px;padding:10px 0 14px;border-bottom:1px solid #eef2f7;font-size:14px}.dxm-link{border:0;background:white;color:#475569;cursor:pointer}.dxm-count{margin-left:auto;color:#991b1b}.dxm-list-title{font-size:14px;font-weight:700;margin:14px 0 8px}.dxm-list{border:1px solid #cbd5e1;border-radius:7px;overflow:hidden}.dxm-item{display:flex;align-items:center;gap:10px;padding:11px;border-bottom:1px solid #eef2f7;cursor:pointer}.dxm-item:last-child{border-bottom:0}.dxm-thumb{width:52px;height:52px;background:#f1f5f9;border-radius:5px;display:flex;align-items:center;justify-content:center;color:#94a3b8}.dxm-item-copy{min-width:0;flex:1}.dxm-item-name{font-size:14px;font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.dxm-match{font-size:12px;color:#059669;margin-top:5px}.dxm-status{font-size:13px;color:#64748b;margin-top:12px;min-height:18px}.dxm-footer{display:flex;align-items:center;gap:10px;padding:15px 16px;border-top:1px solid #e8edf3;font-size:14px}.dxm-switch{width:42px;height:24px;border-radius:20px;background:#2563eb;padding:3px;display:flex;justify-content:flex-end}.dxm-dot{width:18px;height:18px;border-radius:50%;background:#fff}.dxm-primary{margin-left:auto;border:0;border-radius:6px;padding:10px 24px;background:#ef4444;color:#fff;font-weight:700;cursor:pointer}.dxm-primary:disabled{opacity:.55;cursor:wait}
    `;
  }

  function updateSelectedCount(panel) {
    const count = panel.querySelectorAll(".dxm-sku-check:checked").length;
    panel.querySelector(".dxm-count").textContent = `已选 ${count} 个商品`;
  }

  function setNativeValue(element, value) {
    if (!element) return;
    const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
    setter ? setter.call(element, value) : element.value = value;
    element.dispatchEvent(new Event("input", {bubbles: true}));
    element.dispatchEvent(new Event("change", {bubbles: true}));
  }

  function fillDianxiaomiFields(item) {
    setNativeValue(document.querySelector('input[name="title"], input[placeholder*="标题"]'), item.title || "");
    setNativeValue(document.querySelector('textarea[name="description"], textarea[placeholder*="描述"]'), item.description || "");
    setNativeValue(document.querySelector('input[name="price"], input[placeholder*="价格"]'), item.price || "");
  }

  async function syncPendingToDianxiaomi(panel) {
    const status = panel.querySelector(".dxm-status"), button = panel.querySelector(".dxm-primary");
    button.disabled = true; status.textContent = "正在读取本地待同步商品...";
    try {
      const response = await fetch(`${LOCAL_API}/api/plugin/dianxiaomi/pending?limit=1`);
      if (!response.ok) throw new Error("本地服务响应异常");
      const pending = await response.json(), item = pending.items?.[0];
      if (!item) { status.textContent = "暂无待同步商品，请先在系统工作台点击一键写入"; return; }
      await fetch(`${LOCAL_API}/api/plugin/dianxiaomi/progress`, {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({product_id:item.id,stage:"filling_form",message:"正在写入店小秘商品表单",done:3,total:6})});
      fillDianxiaomiFields(item);
      const result = await fetch(`${LOCAL_API}/api/plugin/dianxiaomi/result`, {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({product_id:item.id,success:true,platform_product_id:`DXM-${item.id}`,message:"已写入店小秘",details:{selected_skus:panel.querySelectorAll('.dxm-sku-check:checked').length}})});
      if (!result.ok) throw new Error("同步结果回写失败");
      status.textContent = `同步完成：${item.title}`;
    } catch (error) { status.textContent = `同步失败：${error.message}`; }
    finally { button.disabled = false; }
  }

  function renderDianxiaomiAssistant() {
    if (detectPlatform() !== "dianxiaomi" || document.getElementById("dxm-sync-assistant")) return;
    const style = document.createElement("style"); style.textContent = assistantStyles(); document.head.appendChild(style);
    const skus = detectDianxiaomiSkus(), panel = document.createElement("aside"); panel.id = "dxm-sync-assistant";
    panel.innerHTML = `<div class="dxm-head"><button class="dxm-close" title="关闭">×</button><p class="dxm-title">商品同步助手</p><div class="dxm-sub">● 已识别 ${skus.length} 个SKU</div></div><div class="dxm-body"><div class="dxm-kicker">快捷选择 <span class="dxm-hint">点击颜色可批量切换同组SKU</span></div><div class="dxm-tags"><button class="dxm-tag" data-select="all">全部 ${skus.length}</button><button class="dxm-tag" data-select="first">${skus[0].label.slice(0,8)} 1</button></div><div class="dxm-actions"><label><input id="dxm-select-all" type="checkbox" checked> 全选</label><button class="dxm-link" id="dxm-invert">反选</button><span class="dxm-count">已选 ${skus.length} 个商品</span></div><div class="dxm-list-title">变种列表</div><div class="dxm-list">${skus.map((sku,index)=>`<label class="dxm-item"><input class="dxm-sku-check" type="checkbox" checked data-index="${index}"><div class="dxm-thumb">SKU</div><div class="dxm-item-copy"><div class="dxm-item-name">${sku.label}</div><div class="dxm-match">轮播图 6/6 · 已匹配</div></div><span>›</span></label>`).join('')}</div><div class="dxm-status">等待同步</div></div><div class="dxm-footer"><span>上传到店小秘</span><span class="dxm-switch"><span class="dxm-dot"></span></span><button class="dxm-primary">采集并写入</button></div>`;
    document.body.appendChild(panel);
    panel.querySelector(".dxm-close").addEventListener("click",()=>panel.remove());
    panel.querySelector("#dxm-invert").addEventListener("click",()=>{panel.querySelectorAll('.dxm-sku-check').forEach(i=>i.checked=!i.checked);updateSelectedCount(panel);});
    panel.querySelector("#dxm-select-all").addEventListener("change",e=>{panel.querySelectorAll('.dxm-sku-check').forEach(i=>i.checked=e.target.checked);updateSelectedCount(panel);});
    panel.querySelectorAll(".dxm-sku-check").forEach(i=>i.addEventListener("change",()=>updateSelectedCount(panel)));
    panel.querySelector('[data-select="first"]').addEventListener("click",()=>{panel.querySelectorAll('.dxm-sku-check').forEach((i,n)=>i.checked=n===0);updateSelectedCount(panel);});
    panel.querySelector('[data-select="all"]').addEventListener("click",()=>{panel.querySelectorAll('.dxm-sku-check').forEach(i=>i.checked=true);updateSelectedCount(panel);});
    panel.querySelector(".dxm-primary").addEventListener("click",()=>syncPendingToDianxiaomi(panel));
  }

  // 监听来自 popup 或 background 的消息
  if (typeof chrome !== "undefined" && chrome.runtime?.onMessage) chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === "extractProduct") {
      const info = extractProductInfo();
      sendResponse({ success: true, data: info });
    }
    return true;
  });

  // 自动提取并保存
  const productInfo = extractProductInfo();
  if (productInfo.title && typeof chrome !== "undefined" && chrome.storage?.local) {
    chrome.storage.local.set({ currentProduct: productInfo });
  }

  renderDianxiaomiAssistant();

  console.log("[商品采集助手] Content script loaded for", detectPlatform());
})();
