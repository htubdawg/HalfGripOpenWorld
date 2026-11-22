"""The buck's antlers: one classic set, a typical eight-pointer (on each side a main beam sweeping up, out and back over the ears
and then forward and in, a short brow tine and two long tines standing up from the beam). Rigid on the head: every vertex weighted
wholly to the `head` bone, so the doe's skeleton, poses and painted coat all carry over and the buck is the doe's mesh with these
added (the owner's choice: one good set, no procedural antlers; decision 8 in docs/DEER_HANDOFF.md keeps deer.json frozen).

Each tine is a low-poly tube round a smooth curve, tapering to a point, its base buried in the beam (or, for the beam, in the skull)
so the joins need no welding. They have a texture of their own in the game: `v` runs from the burr (0) to the tips (1) and the
game's antler texture is a gradient along it (dark at the burr, pale at the tips), so the coat's texture is left alone.

Units and frame as deer.py: metres, Y up, facing +Z, the deer's left = +X.
python3 antlers.py            (writes antlers.json and the texture templates texture/antlers_uv_*.png; checks the antlers clear the ears, the neck and the back in every pose)
"""
import os, json, math, numpy as np

SIDES = 6                   # faces round a tube

# The texture (textures/antlers.png in the game, 1024 square): each piece unrolled into an upright strip, its base at the bottom and
# its tip at the top, round the tube from the strip's left edge to its right (the two edges meet). Both antlers share the strips.
# Pixels from the top left: x, y, width, height. The lengths are to one scale (about 15 px a centimetre); the widths wider than the
# pieces are round, for detail across them.
TEX = 1024
PARTS = ['beam', 'burr', 'brow', 'g2', 'g3']
ISLANDS = dict(beam=(48, 56, 280, 920), g2=(392, 56, 220, 300), g3=(392, 420, 200, 230), brow=(680, 56, 170, 120),
			   burr=(680, 250, 300, 60))
LABELS = dict(beam='MAIN BEAM', g2='LONG TINE', g3='SECOND TINE', brow='BROW TINE', burr='BURR (the rough ring at the base)')

# The left antler (+X); the right is its mirror. Each part: control points (x, y, z), the radius at each, and v (burr 0 .. tip 1)
# at its first and last point. The beam's first point is inside the skull (the pedicle).
BEAM = dict(pts=[(0.026, 1.296, 0.610), (0.044, 1.348, 0.604), (0.082, 1.398, 0.590), (0.135, 1.432, 0.590), (0.178, 1.448, 0.625),
				 (0.196, 1.456, 0.685), (0.183, 1.462, 0.752), (0.143, 1.470, 0.805), (0.098, 1.482, 0.830)],
			r=[0.018, 0.019, 0.0165, 0.015, 0.0142, 0.013, 0.0112, 0.0085, 0.0035], v=(0.0, 1.0))
BURR = dict(at=1, r=0.0235, len=0.010)       # the rough ring round the base, at the beam's second point
TINES = [
	# brow tine: short, up and a little forward from low on the beam
	dict(at=0.20, pts=[(0, 0, 0), (-0.004, 0.040, 0.010), (-0.010, 0.072, 0.024)], r=[0.0095, 0.0075, 0.003]),
	# G2: the longest, standing up from the beam where it turns forward, its top leaning in
	dict(at=0.50, pts=[(0, 0, 0), (-0.004, 0.075, 0.006), (-0.014, 0.140, 0.016), (-0.028, 0.182, 0.026)], r=[0.012, 0.0105, 0.0075, 0.003]),
	# G3: a little shorter, further along
	dict(at=0.72, pts=[(0, 0, 0), (-0.004, 0.058, 0.008), (-0.014, 0.108, 0.018), (-0.026, 0.140, 0.026)], r=[0.0105, 0.009, 0.0065, 0.003]),
]

def catmull(P, n):
	"""A smooth curve through the points P, n samples per span (centripetal-free uniform Catmull-Rom; ends repeated)."""
	P = np.asarray(P, float)
	Q = np.vstack([P[0] * 2 - P[1], P, P[-1] * 2 - P[-2]])
	out = []
	for i in range(1, len(Q) - 2):
		p0, p1, p2, p3 = Q[i - 1], Q[i], Q[i + 1], Q[i + 2]
		for k in range(n):
			t = k / n
			out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
	out.append(P[-1])
	return np.array(out)

def resample_r(r, n):
	r = np.asarray(r, float)
	out = []
	for i in range(len(r) - 1):
		for k in range(n):
			t = k / n; t = t * t * (3 - 2 * t)
			out.append(r[i] * (1 - t) + r[i + 1] * t)
	out.append(r[-1])
	return np.array(out)

def tube(C, R, v0, v1):
	"""Rings round the curve C (radii R), parallel-transported so they do not twist; closed to a point at the end. Returns
	positions, normals, v per vertex (burr 0 .. tips 1, for the gradient), triangles (counter-clockwise from outside), and each
	vertex's place on the unrolled strip: round the tube (0 .. 1, the seam's vertices doubled) and along it (base 0 .. tip 1)."""
	n = len(C)
	T = np.gradient(C, axis=0); T /= np.linalg.norm(T, axis=1, keepdims=True)
	up = np.array((0, 0, 1.0)) if abs(T[0][1]) > 0.9 else np.array((0, 1.0, 0))
	N0 = np.cross(T[0], up); N0 /= np.linalg.norm(N0)
	Ns = [N0]
	for i in range(1, n):
		Nn = Ns[-1] - (Ns[-1] @ T[i]) * T[i]; Ns.append(Nn / np.linalg.norm(Nn))
	S = np.linalg.norm(np.diff(C, axis=0), axis=1); s = np.r_[0, np.cumsum(S)]; s /= s[-1]
	pos, nor, vv, ar, al = [], [], [], [], []
	W = SIDES + 1
	for i in range(n - 1):
		B = np.cross(T[i], Ns[i])
		for k in range(W):
			a = 2 * math.pi * k / SIDES + math.pi       # the seam (the strip's edges) on the inner side, out of sight
			d = math.cos(a) * Ns[i] + math.sin(a) * B
			pos.append(C[i] + R[i] * d); nor.append(d); vv.append(v0 + (v1 - v0) * s[i]); ar.append(k / SIDES); al.append(s[i])
	tip = len(pos)
	for k in range(SIDES):                       # a tip vertex per face, so each keeps its own place on the strip
		pos.append(C[-1]); nor.append(T[-1]); vv.append(v1); ar.append((k + 0.5) / SIDES); al.append(1.0)
	tris = []
	for i in range(n - 2):
		for k in range(SIDES):
			a, b = i * W + k, i * W + k + 1
			c, d = a + W, b + W
			tris += [(a, c, b), (b, c, d)]
	last = (n - 2) * W
	for k in range(SIDES):
		tris.append((last + k, tip + k, last + k + 1))
	pos, nor, tris = np.array(pos), np.array(nor), np.array(tris)
	# Winding: make each triangle face away from the curve (outwards).
	fn = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
	out = (nor[tris].mean(1) * fn).sum(1) < 0
	tris[out] = tris[out][:, [0, 2, 1]]
	return pos, nor, np.array(vv), tris, np.array(ar), np.array(al)

def burr(C, R, at, r, length):
	"""The burr: a short, wide, blunt ring round the beam's base."""
	T = C[at + 1] - C[at - 1]; T /= np.linalg.norm(T)
	c = C[at]
	pts = np.array([c - T * length, c, c + T * length * 0.6])
	return tube(pts, np.array([r * 0.85, r, r * 0.7]), 0.0, 0.03)

def build():
	parts = []
	beam_pts = catmull(BEAM["pts"], 2)
	beam_r = resample_r(BEAM["r"], 2)
	parts.append(tube(beam_pts, beam_r, *BEAM['v']))
	bi = BURR["at"] * 2
	parts.append(burr(beam_pts, beam_r, bi, BURR['r'], BURR['len']))
	S = np.r_[0, np.cumsum(np.linalg.norm(np.diff(beam_pts, axis=0), axis=1))]; S /= S[-1]
	for tn in TINES:
		i = int(np.searchsorted(S, tn['at'])); base = beam_pts[i]
		# the tine starts inside the beam (a little below its centre line), so its base is buried
		pts = base + np.array(tn['pts']) + np.array((0, -0.004, 0))
		parts.append(tube(catmull(pts, 2), resample_r(tn["r"], 2), tn['at'] * 0.8, 1.0))
	P, Nn, V, F, UV = [], [], [], [], []
	off = 0
	for name, (p, n, v, f, ar, al) in zip(PARTS, parts):
		x, y, w, h = ISLANDS[name]
		# on the strip: round the tube left to right, the base at the bottom and the tip at the top (UVs from the bottom left)
		UV.append(np.c_[(x + ar * w) / TEX, 1 - (y + (1 - al) * h) / TEX])
		P.append(p); Nn.append(n); V.append(v); F.append(f + off); off += len(p)
	P, Nn, V, F, UV = np.vstack(P), np.vstack(Nn), np.concatenate(V), np.vstack(F), np.vstack(UV)
	# the right antler: mirrored in x, its triangles' winding reversed; it shares the left one's strips
	Pm = P * np.array((-1, 1, 1)); Nm = Nn * np.array((-1, 1, 1)); Fm = F[:, [0, 2, 1]] + len(P)
	return np.vstack([P, Pm]), np.vstack([Nn, Nm]), np.concatenate([V, V]), np.vstack([F, Fm]), np.vstack([UV, UV])

def clearance():
	"""The antlers against the doe's own surface in every pose (both posed on their bones): how far into the body (ears, neck,
	back) any antler point goes. Small overlaps at the pedicles are where they grow from and are left out (within 4 cm of one)."""
	import deer as D
	from preview import skin
	m = load_doe()
	P = build()[0]
	ant = dict(pos=P, nor=np.zeros_like(P), bi=np.tile([D.BI['head'], 0, 0, 0], (len(P), 1)), bw=np.tile([1.0, 0, 0, 0], (len(P), 1)))
	ped = np.array([BEAM['pts'][1], np.array(BEAM['pts'][1]) * (-1, 1, 1)])
	far = np.min(np.linalg.norm(P[:, None] - ped[None], axis=2), axis=1) > 0.04
	out = {}
	for name, pose in D.POSES.items():
		body, _ = skin(m, pose); a, _ = skin(ant, pose)
		from scipy.spatial import cKDTree
		# distance from each antler point to the nearest body vertex not on the head (the head carries the antlers with it)
		W = np.zeros((len(m['pos']), len(D.BONES)))
		for j in range(4): np.add.at(W, (np.arange(len(W)), m['bi'][:, j]), m['bw'][:, j])
		rigid = W[:, D.BI['head']] > 0.98
		tree = cKDTree(body[~rigid])
		d, _ = tree.query(a[far])
		out[name] = (float(d.min()), float(a[:, 1].min()))
	return out

def load_doe():
	here = os.path.dirname(os.path.abspath(__file__))
	J = json.load(open(os.path.join(here, 'deer.json')))
	return dict(pos=np.array(J['pos']).reshape(-1, 3), nor=np.array(J['nor']).reshape(-1, 3), idx=np.array(J['idx']).reshape(-1, 3),
				bi=np.array(J['bi']).reshape(-1, 4), bw=np.array(J['bw']).reshape(-1, 4), reg=np.array(J['reg']), uv=np.array(J['uv']).reshape(-1, 2))

def export(path):
	P, Nn, V, F, UV = build()
	data = dict(bone='head', pos=np.round(P, 4).ravel().tolist(), nor=np.round(Nn, 3).ravel().tolist(), v=np.round(V, 3).tolist(),
				uv=np.round(UV, 5).ravel().tolist(), idx=F.ravel().tolist())
	json.dump(data, open(path, 'w'), separators=(',', ':'))
	return P, F, UV

def templates(F, UV, outdir):
	"""The templates to paint on: uv_guide.png (the strips in flat antler colours, dark at the base to pale at the tips, grey
	between) and uv_template.png (the same with the wireframe and labels)."""
	from PIL import Image, ImageDraw, ImageFont
	import matplotlib
	fdir = os.path.join(os.path.dirname(matplotlib.__file__), 'mpl-data/fonts/ttf')
	os.makedirs(outdir, exist_ok=True)
	stops = [(0, (59, 46, 34)), (0.06, (90, 70, 50)), (0.35, (134, 112, 79)), (0.75, (181, 162, 132)), (1, (230, 220, 198))]
	def grad(t):
		for (a, ca), (b, cb) in zip(stops, stops[1:]):
			if t <= b: k = (t - a) / (b - a); return tuple(int(ca[i] + (cb[i] - ca[i]) * k) for i in range(3))
		return stops[-1][1]
	guide = Image.new('RGB', (TEX, TEX), (128, 128, 128)); d = ImageDraw.Draw(guide)
	tmpl = Image.new('RGB', (TEX, TEX), (255, 255, 255)); dt = ImageDraw.Draw(tmpl)
	for name in PARTS:
		x, y, w, h = ISLANDS[name]
		v0 = 0.0 if name in ('beam', 'burr') else 0.4
		v1 = 0.06 if name == 'burr' else 1.0
		for r in range(h):
			c = grad(v0 + (v1 - v0) * (1 - r / max(1, h - 1)))
			d.line([(x, y + r), (x + w - 1, y + r)], fill=c)
			dt.line([(x, y + r), (x + w - 1, y + r)], fill=tuple(int(255 - (255 - ch) * 0.35) for ch in c))
	px = np.c_[UV[:, 0] * TEX, (1 - UV[:, 1]) * TEX]
	for f in F[:len(F) // 2]:
		p = [tuple(q) for q in px[f]]; dt.line(p + [p[0]], fill=(120, 120, 130), width=1)
	font = ImageFont.truetype(os.path.join(fdir, 'DejaVuSans-Bold.ttf'), 13)
	for name in PARTS:
		x, y, w, h = ISLANDS[name]
		dt.rectangle([x - 3, y - 3, x + w + 3, y + h + 3], outline=(200, 40, 60), width=2)
		dt.text((x, y - 19), LABELS[name], fill=(200, 40, 60), font=font)
		if name != 'burr':
			dt.text((x + 4, y + h - 18), 'base', fill=(90, 90, 100), font=font); dt.text((x + 4, y + 4), 'tip', fill=(90, 90, 100), font=font)
	guide.save(os.path.join(outdir, 'antlers_uv_guide.png'))
	tmpl.save(os.path.join(outdir, 'antlers_uv_template.png'))

if __name__ == '__main__':
	here = os.path.dirname(os.path.abspath(__file__)); os.chdir(here)
	P, F, UV = export('antlers.json')
	templates(F, UV, 'texture')
	print('antlers:', len(P), 'vertices,', len(F), 'triangles; spread', round(2 * P[:, 0].max(), 3), 'm, top', round(P[:, 1].max() - 1.32, 3), 'm above the skull')
	for k, (d, low) in clearance().items():
		print('  %-7s nearest body vertex to an antler %.3f m; lowest antler point %.3f m' % (k, d, low))
