import sys
import os

_base = r'C:\Users\NIDHOGG\Documents\商品上架\backend'
os.chdir(_base)
sys.path.insert(0, _base)

# 强制清除所有 __pycache__
import shutil
for root, dirs, files in os.walk(_base):
    for d in dirs:
        if d == '__pycache__':
            p = os.path.join(root, d)
            shutil.rmtree(p, ignore_errors=True)
            print(f'Cleared {p}')

import uvicorn
from app.main import app
print('Starting server...')
uvicorn.run(app, host='127.0.0.1', port=8000, log_level='warning')
