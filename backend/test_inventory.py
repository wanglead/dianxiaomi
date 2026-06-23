import sys, os, threading, time, httpx
os.chdir(r'C:\Users\NIDHOGG\Documents\商品上架\backend')
sys.path.insert(0, r'C:\Users\NIDHOGG\Documents\商品上架\backend')

import uvicorn
from app.main import app

def start():
    uvicorn.run(app, host='127.0.0.1', port=8000, log_level='warning')

t = threading.Thread(target=start, daemon=True)
t.start()
time.sleep(6)

for p in ['/','/inventory','/api/inventory/records','/api/inventory/alerts']:
    r = httpx.get(f'http://127.0.0.1:8000{p}', timeout=3)
    print(f'{p}: {r.status_code}')

r = httpx.post('http://127.0.0.1:8000/api/inventory/inbound?product_id=1&quantity=100&remark=test')
print(f'Inbound: {r.status_code}', r.json())

r2 = httpx.get('http://127.0.0.1:8000/api/inventory/records?limit=5')
print(f'Records: {r2.status_code}, count={len(r2.json()["records"])}')
print('ALL OK')
