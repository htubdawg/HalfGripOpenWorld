"""Writes viewer/index.html: the viewer template with the frozen mesh (deer.json) and the UV guide put in, one file that opens
straight from disk. The same page is published as an artifact (where it fetches deer.json and uv_guide.png beside it).
python3 build_viewer.py"""
import os, base64
here = os.path.dirname(os.path.abspath(__file__))
t = open(os.path.join(here, 'viewer', 'template.html')).read()
data = open(os.path.join(here, 'deer.json')).read()
guide = 'data:image/png;base64,' + base64.b64encode(open(os.path.join(here, 'texture', 'uv_guide.png'), 'rb').read()).decode()
page = t.replace('__DATA__', data).replace('__GUIDE__', guide)
head = '<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n'
i = page.index('<div class="app">')
out = head + page[:i] + '</head>\n<body>\n' + page[i:] + '\n</body>\n</html>\n'
open(os.path.join(here, 'viewer', 'index.html'), 'w').write(out)
print('viewer/index.html', len(out) // 1024, 'KB')
