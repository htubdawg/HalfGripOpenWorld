"""Software renders of the built deer (out/mesh.npz) in its poses, for looking at before anything goes in the game.
python3 preview.py out.png stand,graze,gather colour 90,0,35,150   (poses; colour | clay; yaw degrees per column)"""
import sys, math, os, numpy as np
import matplotlib; matplotlib.use('Agg')
import matplotlib.pyplot as plt
import deer as D

def skin(m, pose, root=None):
	W = D.world_mats(pose, root); inv = {k: np.linalg.inv(v) for k, v in D.BIND.items()}
	mats = np.array([W[b[0]] @ inv[b[0]] for b in D.BONES])
	p = np.c_[m['pos'], np.ones(len(m['pos']))]
	out = np.zeros((len(p), 3)); nout = np.zeros((len(p), 3))
	for j in range(4):
		M = mats[m['bi'][:, j]]; w = m['bw'][:, j:j + 1]
		out += w * np.einsum('nij,nj->ni', M, p)[:, :3]
		nout += w * np.einsum('nij,nj->ni', M[:, :3, :3], m['nor'])
	return out, nout / (np.linalg.norm(nout, axis=1, keepdims=True) + 1e-9)

def raster(m, pose, yaw, clay, W_=560, H_=420, scale=260, cy=0.72, pitch=0.12, wire=False, palette=None):
	pos, nor = skin(m, pose)
	c, s = math.cos(yaw), math.sin(yaw)
	Ry = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
	cp, sp = math.cos(pitch), math.sin(pitch)
	Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
	V = pos @ Ry.T @ Rx.T; N = nor @ Ry.T @ Rx.T
	L = np.array([0.35, 0.55, 0.75]); L /= np.linalg.norm(L)
	lam = 0.42 + 0.68 * np.clip(N @ L, 0, 1)
	pal = palette or D.PALETTE
	if clay:
		base = np.full((len(V), 3), 0.80)
	else:
		P = np.array([[(pal[r] >> 16 & 255) / 255, (pal[r] >> 8 & 255) / 255, (pal[r] & 255) / 255] for r in D.REGIONS])
		base = P[m['reg']]
	vcol = base * lam[:, None]
	img = np.zeros((H_, W_, 3)); img[:] = 0.83 if clay else (0.30, 0.36, 0.28)
	zb = np.full((H_, W_), -1e9)
	sx = V[:, 0] * scale + W_ / 2; sy = H_ / 2 - (V[:, 1] - cy) * scale
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
		col = w0[..., None] * vcol[t[0]] + w1[..., None] * vcol[t[1]] + w2[..., None] * vcol[t[2]]
		if wire:
			col[np.minimum(np.minimum(w0, w1), w2) < 0.05] *= 0.6
		sub[upd] = zz[upd]; img[y0:y1 + 1, x0:x1 + 1][upd] = col[upd]
	return np.clip(img, 0, 1)

if __name__ == '__main__':
	here = os.path.dirname(os.path.abspath(__file__))
	m = dict(np.load(os.path.join(here, 'out', 'mesh.npz')))
	D.build_prims()
	out = sys.argv[1]; poses = sys.argv[2].split(','); clay = sys.argv[3] == 'clay'
	yaws = [float(a) for a in (sys.argv[4] if len(sys.argv) > 4 else '90,0,35,150').split(',')]
	wire = len(sys.argv) > 5 and sys.argv[5] == 'wire'
	rows = [np.concatenate([raster(m, D.POSES[p], math.radians(y), clay, wire=wire) for y in yaws], 1) for p in poses]
	plt.imsave(out, np.concatenate(rows, 0))
