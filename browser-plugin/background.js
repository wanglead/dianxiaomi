// 商品采集助手 - Background Service Worker
chrome.runtime.onInstalled.addListener(() => {
  console.log("商品采集助手已安装");
});

// 接收来自 content script 的消息
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === "productExtracted") {
    // 保存采集到的商品数据
    chrome.storage.local.set({
      lastCollected: {
        ...message.data,
        collectedAt: new Date().toISOString()
      }
    });
  }
});
