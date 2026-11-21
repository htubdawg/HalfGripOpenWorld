"""UV unwrap for the baked deer, and paint-over templates for an image model (adapted from htubdawg/Claudeinoid's sculpt/uv.py).

Islands, picked per triangle from its dominant bone (and for the trunk, the ear and the tail, from which way the triangle faces).
Each is drawn as seen from outside, never mirrored:
  trunk          a cylinder along the body: the back down the middle (seen from above, head end at the top), the left side to
                 the left of it, the right side to the right, the belly split along the middle at both edges
  rump           from behind                                        chest   from the front (the brisket)
  neck           a cylinder round the neck: the throat in the middle, the seam down the crest; the head end at the top
  head           a cylinder round the head: the top of the head (forehead) in the middle, the eyes and cheeks either side,
                 the seam under the chin; the nose end at the bottom
  nose           the end of the muzzle from the front
  earFrontL/R    each ear's inside, from the front;   earBackL/R  its outside, from behind; tips at the top
  F/H up/mid/low L/R  each leg in three (upper, middle, lower to the hoof), each unrolled round its own bone: the outer side
                 in the middle, the seam on the inner side, the top of the leg at the top
  tailTop        the tail's outer (brown) face;        tailUnder   its white underside (seen when it is raised)
  soles          the four hooves, from the side, laid over each other (all dark)

Usage:  python3 uv.py       (unwraps out/mesh.npz, writes out/deer_uv.npz and the templates in texture/)
"""
import os, json, numpy as np
import deer as D

SIZE = 1024
PAD = 12                       # px between islands
LABEL = 16                     # px for an island's label above it
DENSITY = dict(head=2.2, nose=2.2, neck=1.25, trunk=1.0, rump=1.0, chest=1.0,
			   earFrontL=1.6, earFrontR=1.6, earBackL=1.4, earBackR=1.4,
			   **{l + p + s: 1.4 for l in ('F', 'H') for p in ('up', 'mid', 'low') for s in 'LR'},
			   tailTop=1.5, tailUnder=1.5, soles=1.0)
DARK_LAID = 'soles'                # the hooves (all dark), laid over each other
CHARTS = list(DENSITY)
LABELS = dict(head='HEAD (nose down)', nose='NOSE', neck='NECK (throat mid)', trunk='BODY (back in the middle, head end up)', rump='RUMP', chest='CHEST',
			  earFrontL='L EAR IN', earFrontR='R EAR IN', earBackL='L EAR OUT', earBackR='R EAR OUT',
			  **{l + p + s: s + ' ' + ('FRONT' if l == 'F' else 'HIND') + ' ' + {'up': 'UPPER', 'mid': 'MID', 'low': 'LOWER'}[p] for l in ('F', 'H') for p in ('up', 'mid', 'low') for s in 'LR'}, tailTop='TAIL', tailUnder='TAIL UNDER', soles='HOOVES')

def _unit(v): v = np.asarray(v, float); return v / np.linalg.norm(v)

def _ortho(v, *against):
	v = np.asarray(v, float)
	for a in against: v = v - (v @ a) * a
	return _unit(v)

X, Y, Z = np.eye(3)

def ear_frame(side):
	s = 1 if side == 'L' else -1
	d = _unit((s * 0.55, 0.78, -0.30)); R = D.axes(d, (s * 0.35, 0.0, 1.0))
	return R[:, 0], d                                   # the opening's facing, the ear's length (up to the tip)

def tail_frame():
	T1, T2 = D.JP['tail1'], D.JP['tail2']
	d = T1 - T2; R = D.axes(d, (0, 0.30, 1.0))
	return R[:, 0], _unit(T2 - T1)                      # the underside's facing (towards the body), down the tail

HEAD_O = np.array((0, 1.255, 0.612))
HEAD_A = _unit(D.MUZ_B - np.array((0, 1.27, 0.56)))     # back of the skull to the nose

def chart_defs():
	JP = D.JP
	C = {'trunk': dict(kind='cyl', O=np.array((0, 0.73, 0.0)), a=-Z, c=Y, r=-X),
		 'rump': dict(kind='plane', r=-X, a=-Y), 'chest': dict(kind='plane', r=X, a=-Y),
		 'soles': dict(kind='sole')}
	na = _unit(D.NECK_A - D.NECK_B)
	C['neck'] = dict(kind='cyl', O=D.NECK_B, a=na, c=_ortho((0, -0.45, 0.89), na), r=X)
	C['head'] = dict(kind='cyl', O=HEAD_O, a=HEAD_A, c=_ortho(Y, HEAD_A), r=X)
	C['nose'] = dict(kind='plane', r=X, a=_ortho(-Y, HEAD_A))
	for side in 'LR':
		face, d = ear_frame(side)
		C['earFront' + side] = dict(kind='plane', r=np.cross(-face, d), a=-d)
		C['earBack' + side] = dict(kind='plane', r=np.cross(face, d), a=-d)
	under, down = tail_frame()
	C['tailTop'] = dict(kind='plane', r=np.cross(-under, -down) * -1, a=down)
	C['tailUnder'] = dict(kind='plane', r=np.cross(under, -down) * -1, a=down)
	for side, s in (('L', 1), ('R', -1)):
		toe = lambda j: JP[j + side] + np.array((0, -0.066, 0.048))
		for name, j0, j1 in (('Fup', 'shoulder', 'elbow'), ('Fmid', 'elbow', 'carpus'), ('Flow', 'carpus', None),
							 ('Hup', 'thigh', 'stifle'), ('Hmid', 'stifle', 'hock'), ('Hlow', 'hock', None)):
			O = JP[j0 + side]; a = _unit((JP[j1 + side] if j1 else toe('fetlock' if name[0] == 'F' else 'hfetlock')) - O)
			c = _ortho((s, 0, 0), a); r = _ortho((0, 0, -s), a, c)
			C[name + side] = dict(kind='cyl', O=O, a=a, c=c, r=r)
	return C

BONE_CHART = {'hips': 'trunk', 'spine': 'trunk', 'chest': 'trunk', 'neck1': 'neck', 'neck2': 'neck', 'head': 'head',
			  'earL': 'earL', 'earR': 'earR', 'tail1': 'tail', 'tail2': 'tail'}
for s in 'LR':
	BONE_CHART.update({'shoulder' + s: 'Fup' + s, 'elbow' + s: 'Fmid' + s, 'carpus' + s: 'Flow' + s, 'fetlock' + s: 'Flow' + s,
					   'thigh' + s: 'Hup' + s, 'stifle' + s: 'Hmid' + s, 'hock' + s: 'Hlow' + s, 'hfetlock' + s: 'Hlow' + s})

def assign(V, F, bi, bw, reg):
	W = np.zeros((len(V), len(D.BONES)))
	for j in range(4): np.add.at(W, (np.arange(len(V)), bi[:, j]), bw[:, j])
	dom = W[F].sum(1).argmax(1)
	fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
	fn /= np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
	under, _ = tail_frame()
	names = []
	for fi, b in enumerate(dom):
		c = BONE_CHART[D.BONES[b][0]]; n = fn[fi]
		cy = V[F[fi], 1].mean()
		if c[:3] in ('Fup', 'Hup') and cy > D.JP[('shoulder' if c[0] == 'F' else 'thigh') + c[-1]][1]:
			c = 'trunk'                                     # body skin above the leg's top joint
		if c[:4] in ('Flow', 'Hlow') and D.REGIONS[reg[F[fi][0]]] == 'dark':
			c = 'soles'                                     # the hooves are all dark: laid over each other
		elif c == 'trunk':
			if abs(n[2]) > 0.6: c = 'chest' if n[2] > 0 else 'rump'
		elif c == 'head' and (V[F[fi], 1].min() < 1.10 or ((V[F[fi]] - HEAD_O) @ HEAD_A).mean() < -0.035):
			c = 'neck'                                      # below the head or behind the skull: the cylinder round the head is
			                                                # pinched there, the neck's is not
		elif c == 'head' and n @ HEAD_A > 0.72:
			c = 'nose'
		elif c in ('earL', 'earR'):
			face, _ = ear_frame(c[-1]); c = ('earFront' if n @ face > 0 else 'earBack') + c[-1]
		elif c == 'tail':
			c = 'tailUnder' if n @ under > 0 else 'tailTop'
		names.append(c)
	return np.array(names)

def unwrap(mesh):
	V, F = mesh['pos'], mesh['idx']
	names = assign(V, F, mesh['bi'], mesh['bw'], mesh['reg'])
	C = chart_defs()
	UVc = np.zeros((len(F), 3, 2)); wrap = np.zeros((len(F), 3), int)
	for cn, cd in C.items():
		m = names == cn
		if not m.any(): continue
		P = V[F[m]]
		if cd['kind'] == 'plane':
			UVc[m, :, 0] = P @ cd['r']; UVc[m, :, 1] = P @ cd['a']
		elif cd['kind'] == 'sole':
			# each hoof from the side, round its own toe, so the four lie over each other
			ctr = P.mean(1); hz = np.where(ctr[:, 2] > 0, 0.37, -0.45)
			UVc[m, :, 0] = P[..., 2] - hz[:, None]; UVc[m, :, 1] = -P[..., 1]
		else:
			q = P - cd['O']
			th = np.arctan2(q @ cd['r'], q @ cd['c'])
			radial = q - (q @ cd['a'])[..., None] * cd['a']
			# each point unrolled at its own distance from the axis (a tapering leg comes out tapering, its texels even); per
			# vertex, so the triangles round a vertex agree on where it is
			R = np.linalg.norm(radial, axis=-1)
			# each triangle kept whole: its corners' angles taken round its first corner's, then the triangle put back on the
			# island's side of the seam; the turns added (-1, 0, 1) split the vertices at the seam
			t0 = th[:, :1]
			t2 = t0 + (th - t0 + np.pi) % (2 * np.pi) - np.pi
			mid = t2.mean(1, keepdims=True)
			t2 = t2 - 2 * np.pi * np.round(mid / (2 * np.pi))
			UVc[m, :, 0] = t2 * R; UVc[m, :, 1] = q @ cd['a']; wrap[m] = np.round((t2 - th) / (2 * np.pi)).astype(int)
	present = [c for c in CHARTS if (names == c).any()]
	box = {}
	for cn in present:
		m = names == cn; u, d = UVc[m, :, 0], UVc[m, :, 1]
		box[cn] = (u.min(), u.max(), d.min(), d.max())

	def layout(s, order):
		pos = {}; x = PAD; y = PAD; row_h = 0
		for cn in order:
			u0, u1, d0, d1 = box[cn]; k = s * DENSITY[cn]
			w, h = (u1 - u0) * k, (d1 - d0) * k
			if x + w + PAD > SIZE: x = PAD; y += row_h + PAD + LABEL; row_h = 0
			pos[cn] = (x, y + LABEL, k); x += w + PAD; row_h = max(row_h, h)
		return pos, y + LABEL + row_h + PAD
	rng = np.random.default_rng(7)
	tall = sorted(present, key=lambda c: -(box[c][3] - box[c][2]) * DENSITY[c])
	orders = [tall] + [list(rng.permutation(present)) for _ in range(300)]
	best = None
	for order in orders:
		lo, hi = 50.0, 3000.0
		for _ in range(36):
			mid = (lo + hi) / 2
			if layout(mid, order)[1] <= SIZE and all(p[0] + (box[c][1] - box[c][0]) * p[2] + PAD <= SIZE for c, p in layout(mid, order)[0].items()): lo = mid
			else: hi = mid
		if best is None or lo > best[0]: best = (lo, order)
	pos, _ = layout(*best)

	px = np.zeros_like(UVc)
	for cn in present:
		m = names == cn; x0, y0, k = pos[cn]; u0, _, d0, _ = box[cn]
		px[m, :, 0] = x0 + (UVc[m, :, 0] - u0) * k
		px[m, :, 1] = y0 + (UVc[m, :, 1] - d0) * k

	key = {}; src = []; uv = []; F2 = np.empty_like(F)
	ci = {c: i for i, c in enumerate(CHARTS)}
	for fi, f in enumerate(F):
		for j, v in enumerate(f):
			kk = (int(v), ci[names[fi]], int(wrap[fi, j]))
			if kk not in key:
				key[kk] = len(src); src.append(int(v)); uv.append(px[fi, j])
			F2[fi, j] = key[kk]
	src = np.array(src); uv = np.array(uv)
	out = {k: mesh[k][src] for k in ('pos', 'nor', 'bi', 'bw', 'reg')}
	out['idx'] = F2
	out['uv'] = np.c_[uv[:, 0] / SIZE, 1 - uv[:, 1] / SIZE]
	out['chart'] = np.array([ci[n] for n in names])
	meta = {cn: dict(x=pos[cn][0], y=pos[cn][1], w=(box[cn][1] - box[cn][0]) * pos[cn][2],
					 h=(box[cn][3] - box[cn][2]) * pos[cn][2]) for cn in present}
	return out, meta

# ---------------------------------------------------------------- templates
GUIDE = dict(coat=(176, 124, 80), ear=(214, 190, 168), white=(255, 255, 255), dark=(28, 22, 18))

def draw_templates(mesh, meta, outdir):
	from PIL import Image, ImageDraw, ImageFont
	import matplotlib
	fdir = os.path.join(os.path.dirname(matplotlib.__file__), 'mpl-data/fonts/ttf')
	SS = 2; N = SIZE * SS
	UV = np.c_[mesh['uv'][:, 0] * N, (1 - mesh['uv'][:, 1]) * N]
	F = mesh['idx']
	freg = np.array([D.REGIONS[mesh['reg'][f[0]]] for f in F], dtype=object)

	def base(bg):
		im = Image.new('RGB', (N, N), bg); d = ImageDraw.Draw(im)
		for i in range(len(F)):
			d.polygon([tuple(p) for p in UV[F[i]]], fill=GUIDE[freg[i]])
		return im, d

	im, d = base((128, 128, 128))
	im.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(outdir, 'uv_guide.png'))

	im, d = base((255, 255, 255))
	for f in F:
		p = [tuple(x) for x in UV[f]]; d.line(p + [p[0]], fill=(120, 120, 130), width=1)
	font = ImageFont.truetype(os.path.join(fdir, 'DejaVuSans-Bold.ttf'), 10 * SS)
	small = ImageFont.truetype(os.path.join(fdir, 'DejaVuSans.ttf'), 9 * SS)
	for cn, m in meta.items():
		x, y, w, h = (m[k] * SS for k in ('x', 'y', 'w', 'h'))
		d.rectangle([x - 4, y - 4, x + w + 4, y + h + 4], outline=(200, 40, 60), width=2)
		d.text((x, y - 15 * SS), LABELS[cn], fill=(200, 40, 60), font=font)
		if cn[1:4] in ('up', 'mid', 'low') or cn == 'neck' or cn == 'head':
			edge, mid = {'neck': ('crest', 'throat'), 'head': ('chin', 'forehead')}.get(cn, ('inner', 'outer'))
			d.text((x + 2, y + h - 12 * SS), edge, fill=(90, 90, 100), font=small)
			d.text((x + w / 2 - 14 * SS, y + h - 12 * SS), mid, fill=(90, 90, 100), font=small)
	im.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(outdir, 'uv_template.png'))

	ov = Image.new('RGBA', (N, N), (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
	for f in F:
		p = [tuple(x) for x in UV[f]]; d.line(p + [p[0]], fill=(255, 0, 90, 200), width=2)
	ov.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(outdir, 'uv_wire_overlay.png'))

def stretch(mesh):
	"""How even the texel density is: per triangle, its UV area over its 3D area, relative to its island's median (1 = even)."""
	V, F, UV = mesh['pos'], mesh['idx'], mesh['uv']
	a3 = 0.5 * np.linalg.norm(np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]), axis=1)
	e1, e2 = UV[F[:, 1]] - UV[F[:, 0]], UV[F[:, 2]] - UV[F[:, 0]]
	a2 = 0.5 * np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0])
	r = a2 / np.maximum(a3, 1e-12)
	out = {}
	for ci, cn in enumerate(CHARTS):
		m = mesh['chart'] == ci
		if m.any(): out[cn] = r[m] / np.median(r[m])
	return out

def overlaps(mesh, res=512):
	"""Pixels covered by two triangles of one island or of two (the soles, laid over each other on purpose, left out)."""
	from PIL import Image, ImageDraw
	cnt = np.zeros((res, res), np.int32)
	UV = np.c_[mesh['uv'][:, 0] * res, (1 - mesh['uv'][:, 1]) * res]
	soles = CHARTS.index('soles')
	for fi, f in enumerate(mesh['idx']):
		if mesh['chart'][fi] == soles: continue
		P = UV[f]; c = P.mean(0); d = P - c; L = np.linalg.norm(d, axis=1, keepdims=True)
		P = c + d * np.maximum(L - 1.0, 0) / np.maximum(L, 1e-9)       # 1 px in, so neighbours sharing an edge do not count
		im = Image.new('L', (res, res), 0); ImageDraw.Draw(im).polygon([tuple(p) for p in P], fill=1)
		cnt += np.asarray(im, np.int32)
	where = {}
	ys, xs = np.nonzero(cnt > 1)
	return int((cnt > 1).sum()), int((cnt > 0).sum())

if __name__ == '__main__':
	here = os.path.dirname(os.path.abspath(__file__)); os.chdir(here)
	D.build_prims()
	m = dict(np.load('out/mesh.npz'))
	m, meta = unwrap(m)
	np.savez('out/deer_uv.npz', **m)
	D.export(m, 'deer.json')                           # the frozen build the templates (and any texture painted on them) fit
	os.makedirs('texture', exist_ok=True)
	draw_templates(m, meta, 'texture')
	json.dump(meta, open('texture/uv_layout.json', 'w'), indent=1)
	bad = {cn: float((s < 0.25).mean() + (s > 4).mean()) for cn, s in stretch(m).items()}
	print('verts', len(m['pos']), 'tris', len(m['idx']), 'islands', len(meta))
	o, c = overlaps(m); print('overlapping pixels', o, 'of', c, 'covered (at 512)')
	print('share of triangles squashed or stretched over 4x, per island:', {k: round(v, 3) for k, v in bad.items() if v > 0})
