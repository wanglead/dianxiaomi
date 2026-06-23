import os
import sys

os.chdir(r"C:\Users\NIDHOGG\Documents\商品上架\backend")
sys.path.insert(0, r"C:\Users\NIDHOGG\Documents\商品上架\backend")

from fastapi.testclient import TestClient

from app.core.database import init_db
from app.main import app


init_db()
client = TestClient(app)


def test_dianxiaomi_prepare_pending_and_result_loop():
    product_payload = {
        "title": "轻风雕塑摆件",
        "description": "用于店小秘同步测试的商品",
        "original_price": 12.5,
        "selling_price": 19.9,
        "currency": "CNY",
        "images": [
            "https://example.com/a.jpg",
            "https://example.com/b.jpg",
        ],
        "variants": [{"name": "颜色", "value": "轻风雕塑", "stock": 10}],
        "category": "家居装饰",
        "collection_method": "browser_plugin",
        "source_url": "https://example.com/product/1",
        "source_platform": "temu",
        "stock": 10,
    }

    import_resp = client.post(
        "/api/products/collect/by-plugin",
        json={
            "token": "plugin-token-change-in-production",
            "products": [product_payload],
        },
    )
    assert import_resp.status_code == 200
    assert import_resp.json()["imported_count"] >= 1

    list_resp = client.get("/api/products?page_size=1&keyword=轻风雕塑")
    assert list_resp.status_code == 200
    product_id = list_resp.json()["items"][0]["id"]

    process_resp = client.post(
        "/api/products/process",
        json={"product_ids": [product_id], "mode": "image_fill"},
    )
    assert process_resp.status_code == 200
    assert process_resp.json()["updated_count"] == 1

    prepare_resp = client.post(
        "/api/products/sync/dianxiaomi/prepare",
        json={"product_ids": [product_id], "target_store": "Temu半托管 / 半托-1"},
    )
    assert prepare_resp.status_code == 200
    assert prepare_resp.json()["prepared_count"] == 1

    pending_resp = client.get("/api/plugin/dianxiaomi/pending?limit=5")
    assert pending_resp.status_code == 200
    pending_items = pending_resp.json()["items"]
    assert any(item["id"] == product_id for item in pending_items)

    progress_resp = client.post(
        "/api/plugin/dianxiaomi/progress",
        json={
            "product_id": product_id,
            "stage": "uploading_images",
            "message": "轮播图生成进度 3/6",
            "done": 3,
            "total": 6,
        },
    )
    assert progress_resp.status_code == 200
    assert progress_resp.json()["status"] == "syncing"

    result_resp = client.post(
        "/api/plugin/dianxiaomi/result",
        json={
            "product_id": product_id,
            "success": True,
            "platform_product_id": "DXM-LOCAL-1",
            "message": "已写入店小秘",
            "details": {"matched_skus": 1},
        },
    )
    assert result_resp.status_code == 200
    assert result_resp.json()["status"] == "listed"


if __name__ == "__main__":
    test_dianxiaomi_prepare_pending_and_result_loop()
    print("ALL OK")
