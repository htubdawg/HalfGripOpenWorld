"""Renders of the three deer as the game draws them: the doe (deer.json, the painted coat), the buck (the doe at its size with the
antlers from antlers.json, their gradient) and the fawn (the doe shrunk), in the poses, and a group side by side on grass.
python3 render_herd.py out.png [poses] [yaws]        e.g.  python3 render_herd.py ../../out/buck.png stand,graze,gather 90,30,150
python3 render_herd.py out.png group                 a doe, her fawn and two bucks side by side
Needs numpy, scipy, matplotlib, pillow."""
import sys, os, math, json, numpy as np
import matplotlib; matplotlib.use('Agg')
import matplotlib.pyplot as plt
from PIL import Image
import deer as D
from preview import skin
from antlers import load_doe

HERE = os.path.dirname(os.path.abspath(__file__))
COAT = np.asarray(Image.open(os.path.join(HERE, '..', '..', 'OpenWorld', 'textures', 'deer.png')).convert('RGB'), float) / 255
# The antlers' gradient from the burr (v 0) to the tips (v 1): the same stops as DeerMesh.AntlerTexture in the game.
ANTLER = [(0.00, 0x3b2e22), (0.06, 0x5a4632), (0.35, 0x86704f), (0.75, 0xb5a284), (1.00, 0xe6dcc6)]

def antler_colour(v):
	v = np.clip(v, 0, 1); out = np.zeros((len(v), 3))
	for (a, ca), (b, cb) in zip(ANTLER, ANTLER[1:]):
		m = (v >= a) & (v <= b); t = ((v[m] - a) / (b - a))[:, None]
		c0 = np.array([(ca >> 16) & 255, (ca >> 8) & 255, ca & 255]) / 255; c1 = np.array([(cb >> 16) & 255, (cb >> 8) & 255, cb & 255]) / 255
		out[m] = c0 * (1 - t) + c1 * t
	return out

def load_antlers():
	J = json.load(open(os.path.join(HERE, 'antlers.json')))
	P = np.array(J['pos']).reshape(-1, 3); n = len(P)
	return dict(pos=P, nor=np.array(J['nor']).reshape(-1, 3), idx=np.array(J['idx']).reshape(-1, 3), v=np.array(J['v']),
				bi=np.tile([D.BI[J['bone']], 0, 0, 0], (n, 1)), bw=np.tile([1.0, 0, 0, 0], (n, 1)))

def draw(img, zb, m, pose, kind, ox, yaw, pitch, scale, cy, root=None, size=1.0, light=(0.35, 0.55, 0.75)):
	"""Rasterises one mesh into img/zb (the coat sampled per pixel from its UVs, or the antlers' gradient)."""
	H_, W_ = zb.shape
	pos, nor = skin(m, pose, root) if root is not None else skin(m, pose)
	pos = pos * size
	c, s = math.cos(yaw), math.sin(yaw); Ry = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
	cp, sp = math.cos(pitch), math.sin(pitch); Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
	V = pos @ Ry.T @ Rx.T; N = nor @ Ry.T @ Rx.T
	L = np.array(light, float); L /= np.linalg.norm(L)
	lam = 0.45 + 0.65 * np.clip(N @ L, 0, 1)
	sx = V[:, 0] * scale + ox; sy = H_ / 2 - (V[:, 1] - cy) * scale
	acol = antler_colour(m['v']) if kind == 'antler' else None
	for t in m['idx']:
		p = np.c_[sx[t], sy[t]]; z = V[t, 2]
		e, f = p[1] - p[0], p[2] - p[0]
		area = e[0] * f[1] - e[1] * f[0]
		if area >= 0: continue
		x0, x1 = int(max(0, math.floor(p[:, 0].min()))), int(min(W_ - 1, math.ceil(p[:, 0].max())))
		y0, y1 = int(max(0, math.floor(p[:, 1].min()))), int(min(H_ - 1, math.ceil(p[:, 1].max())))
		if x1 < x0 or y1 < y0: continue
		xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
		w0 = ((p[1, 0] - xs) * (p[2, 1] - ys) - (p[1, 1] - ys) * (p[2, 0] - xs)) / area
		w1 = ((p[2, 0] - xs) * (p[0, 1] - ys) - (p[2, 1] - ys) * (p[0, 0] - xs)) / area
		w2 = 1 - w0 - w1
		msk = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
		zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
		sub = zb[y0:y1 + 1, x0:x1 + 1]; upd = msk & (zz > sub)
		if not upd.any(): continue
		sh = w0 * lam[t[0]] + w1 * lam[t[1]] + w2 * lam[t[2]]
		if kind == 'antler':
			col = w0[..., None] * acol[t[0]] + w1[..., None] * acol[t[1]] + w2[..., None] * acol[t[2]]
		else:
			uv = w0[..., None] * m['uv'][t[0]] + w1[..., None] * m['uv'][t[1]] + w2[..., None] * m['uv'][t[2]]
			Hc, Wc = COAT.shape[:2]
			col = COAT[np.clip(((1 - uv[..., 1]) * Hc).astype(int), 0, Hc - 1), np.clip((uv[..., 0] * Wc).astype(int), 0, Wc - 1)]
		col = col * sh[..., None]
		sub[upd] = zz[upd]; img[y0:y1 + 1, x0:x1 + 1][upd] = col[upd]

def frame(W_=520, H_=420, bg=(0.30, 0.36, 0.28)):
	img = np.zeros((H_, W_, 3)); img[:] = bg
	return img, np.full((H_, W_), -1e9)

def deer(img, zb, kind, pose, ox, yaw, pitch=0.12, scale=240, cy=0.80, doe=None, ant=None):
	size = dict(doe=1.0, buck=1.06, fawn=0.6)[kind]
	draw(img, zb, doe, pose, 'coat', ox, yaw, pitch, scale, cy, size=size)
	if kind == 'buck':
		draw(img, zb, ant, pose, 'antler', ox, yaw, pitch, scale, cy, size=size)

if __name__ == '__main__':
	D.build_prims()
	doe, ant = load_doe(), load_antlers()
	out = sys.argv[1]
	if len(sys.argv) > 2 and sys.argv[2] == 'group':
		rows = []
		for yaw in (90, 35):
			img, zb = frame(1400, 460)
			for kind, x, pose in (('buck', 230, 'alert'), ('doe', 560, 'stand'), ('fawn', 800, 'stand'), ('buck', 1080, 'graze')):
				deer(img, zb, kind, D.POSES[pose], x, math.radians(yaw), scale=230, cy=0.85, doe=doe, ant=ant)
			rows.append(img)
		plt.imsave(out, np.clip(np.concatenate(rows, 0), 0, 1))
		sys.exit()
	poses = (sys.argv[2] if len(sys.argv) > 2 else 'stand,alert,graze,gather,extend').split(',')
	yaws = [float(a) for a in (sys.argv[3] if len(sys.argv) > 3 else '90,30,0,150').split(',')]
	kind = sys.argv[4] if len(sys.argv) > 4 else 'buck'
	rows = []
	for p in poses:
		cells = []
		for y in yaws:
			img, zb = frame()
			deer(img, zb, kind, D.POSES[p], img.shape[1] / 2, math.radians(y), doe=doe, ant=ant)
			cells.append(img)
		rows.append(np.concatenate(cells, 1))
	plt.imsave(out, np.clip(np.concatenate(rows, 0), 0, 1))
