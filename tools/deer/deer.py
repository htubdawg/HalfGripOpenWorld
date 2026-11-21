"""A low-poly white-tailed deer (a doe), sculpted the way htubdawg/Claudeinoid's human is.
1. Body = smooth union of anatomical primitives (signed distance field), each with bone weights and a colour region.
2. Surface extracted with surface nets on a fine grid.
3. Quadric-error decimation down to a game budget (detail weighted to the head, ears and the thin legs), region borders kept.
4. Skin weights from which primitives own each vertex (top four bones); vertices split along region borders (one flat colour each).
Units: metres, Y up, faces +Z, hooves on y = 0, the deer's LEFT = +X. A doe: about 0.95 m at the withers, 1.8 m nose to tail.
Bind rotations are all zero, so a bone's local axes are the world's: rotating a downward bone by +x swings it backwards (-z),
an upward bone by +x tips it forwards (+z)."""
import numpy as np, math, json, sys, time, os
from meshtools import ss, f_ell, f_cone, f_sph, smin_exp, smax_poly, surface_nets, decimate, vertex_normals, split_by_region

K_BLEND = 0.014              # smooth-union radius
K_DETAIL = 0.004             # tight blend for the face's small parts

def rot(x, y, z):
	cx, sx = math.cos(x), math.sin(x); cy, sy = math.cos(y), math.sin(y); cz, sz = math.cos(z), math.sin(z)
	Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
	Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
	Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
	return Rx @ Ry @ Rz

# ---------------------------------------------------------------- skeleton (world positions in the bind pose)
JOINTS = [('hips', None, (0, 0.86, -0.40)), ('spine', 'hips', (0, 0.88, -0.05)), ('chest', 'spine', (0, 0.88, 0.28)),
		  ('neck1', 'chest', (0, 0.76, 0.44)), ('neck2', 'neck1', (0, 0.98, 0.52)), ('head', 'neck2', (0, 1.20, 0.58)),
		  ('earL', 'head', (0.045, 1.31, 0.565)), ('earR', 'head', (-0.045, 1.31, 0.565)),
		  ('tail1', 'hips', (0, 0.90, -0.60)), ('tail2', 'tail1', (0, 0.81, -0.64))]
for side, s in (('L', 1), ('R', -1)):
	JOINTS += [('shoulder' + side, 'chest', (s * 0.10, 0.66, 0.40)), ('elbow' + side, 'shoulder' + side, (s * 0.09, 0.48, 0.30)),
			   ('carpus' + side, 'elbow' + side, (s * 0.08, 0.27, 0.33)), ('fetlock' + side, 'carpus' + side, (s * 0.08, 0.075, 0.34)),
			   ('thigh' + side, 'hips', (s * 0.10, 0.72, -0.42)), ('stifle' + side, 'thigh' + side, (s * 0.11, 0.52, -0.30)),
			   ('hock' + side, 'stifle' + side, (s * 0.085, 0.37, -0.53)), ('hfetlock' + side, 'hock' + side, (s * 0.08, 0.075, -0.49))]
JP = {n: np.array(p, float) for n, _, p in JOINTS}
BONES = [(n, par, tuple(JP[n] - (JP[par] if par else 0)), (0, 0, 0)) for n, par, _ in JOINTS]
BI = {b[0]: i for i, b in enumerate(BONES)}

def world_mats(pose=None, root=None):
	"""Each bone's world matrix in a pose (absolute local Euler rotations; bones not listed keep the bind's zero). root: an
	optional (offset, rotation) put on the hips, for poses that move the whole body (lying down)."""
	pose = pose or {}
	M = {}
	for name, parent, off, r in BONES:
		L = np.eye(4); L[:3, :3] = rot(*pose.get(name, r)); L[:3, 3] = off
		if parent is None and root is not None:
			T = np.eye(4); T[:3, :3] = rot(*root[1]); T[:3, 3] = root[0]; L = T @ L
		M[name] = M[parent] @ L if parent else L
	return M
BIND = world_mats()

# ---------------------------------------------------------------- primitives
PRIMS = []   # (fn, bones:[(name, weight | fn(t,P))], region: str | fn(P,t)->array)
DETAIL = []  # indices of the face's small parts, blended with a tight radius
def add(fn, bones, region, detail=False):
	if isinstance(bones, str): bones = [(bones, 1.0)]
	if detail: DETAIL.append(len(PRIMS))
	PRIMS.append((fn, bones, region))

def chain(prev, bone, child, pw=0.5):
	"""Weights for a primitive spanning bone->child, blending into the neighbours at each end."""
	out = []
	wp = (lambda t, P: pw * (1 - ss(0, 0.2, t))) if prev else (lambda t, P: 0 * t)
	wc = (lambda t, P: 0.5 * ss(0.8, 1, t)) if child else (lambda t, P: 0 * t)
	if prev: out.append((prev, wp))
	out.append((bone, lambda t, P: 1 - wp(t, P) - wc(t, P)))
	if child: out.append((child, wc))
	return out

def body_w():
	"""The trunk's weights along its length: hips at the back, chest at the front, the spine between."""
	wh = lambda t, P: 1 - ss(-0.36, -0.10, P[:, 2])
	wc = lambda t, P: ss(0.02, 0.24, P[:, 2])
	return [('hips', wh), ('chest', wc), ('spine', lambda t, P: 1 - wh(t, P) - wc(t, P))]

def axes(d, facing):
	"""A rotation whose local y is along d and local x faces `facing` (made square to d): for flat parts (ears, tail)."""
	y = np.array(d, float); y /= np.linalg.norm(y)
	x = np.array(facing, float); x -= (x @ y) * y; x /= np.linalg.norm(x)
	return np.c_[x, y, np.cross(x, y)]

def trunk_region(P, t):
	x, y, z = P[:, 0], P[:, 1], P[:, 2]
	belly = (y < 0.575 + 0.05 * ss(-0.05, -0.40, z)) & (z > -0.40) & (z < 0.28)
	rump = (z < -0.565) & (np.abs(x) < 0.045) & (y > 0.70) & (y < 0.86)
	return np.where(belly | rump, 'white', 'coat')

def inner(s, x0):
	"""The inner side of a leg (towards the body's middle) is white."""
	return lambda P, t: np.where(s * P[:, 0] < s * x0 - 0.022, 'white', 'coat')

NECK_A, NECK_B = np.array((0, 0.80, 0.40)), np.array((0, 1.20, 0.60))
def neck_region(P, t):
	axis = NECK_A[None] + t[:, None] * (NECK_B - NECK_A)[None]
	under = (P - axis) @ np.array((0, -0.45, 0.89))          # towards the throat (down and forward of the neck's axis)
	return np.where((t > 0.66) & (t < 0.9) & (under > 0.035), 'white', 'coat')

EYES = [(s * 0.056, 1.284, 0.664) for s in (-1, 1)]
def head_region(P, t):
	ring = np.zeros(len(P), bool)
	for e in EYES: ring |= np.linalg.norm(P - np.array(e), axis=1) < 0.027
	return np.where(ring, 'white', 'coat')

MUZ_A, MUZ_B = np.array((0, 1.25, 0.665)), np.array((0, 1.172, 0.832))
def muzzle_region(P, t):
	return np.where((t > 0.80) & (t < 0.90), 'white', 'coat')     # the pale band behind the nose

def jaw_region(P, t):
	return np.where((t > 0.62) | (P[:, 1] < 1.175), 'white', 'coat')   # the white chin

def hoof_region(P, t):
	return np.where(P[:, 1] < 0.047, 'dark', 'coat')

def build_prims():
	PRIMS.clear(); DETAIL.clear()
	# ---- trunk
	add(f_ell((0, 0.705, 0.10), (0.160, 0.205, 0.36)), body_w(), trunk_region)                  # rib cage (deep)
	add(f_ell((0, 0.745, -0.27), (0.142, 0.165, 0.25)), body_w(), trunk_region)                 # flank (the belly tucked up)
	add(f_ell((0, 0.700, 0.27), (0.138, 0.180, 0.18)), body_w(), trunk_region)                  # chest
	add(f_ell((0, 0.610, 0.37), (0.090, 0.095, 0.090)), 'chest', trunk_region)                   # brisket
	add(f_ell((0, 0.850, 0.22), (0.075, 0.080, 0.20)), 'chest', trunk_region)                    # withers
	add(f_ell((0, 0.850, -0.12), (0.100, 0.065, 0.34)), body_w(), trunk_region)                 # back
	add(f_ell((0, 0.835, -0.46), (0.112, 0.085, 0.135)), 'hips', trunk_region)                   # rump
	for side, s in (('L', 1), ('R', -1)):
		add(f_ell((s * 0.080, 0.730, -0.445), (0.088, 0.170, 0.155)), [('hips', 0.55), ('thigh' + side, 0.45)], trunk_region)   # haunch
		add(f_ell((s * 0.098, 0.735, 0.325), (0.050, 0.150, 0.090), rot(0.35, 0, 0)), [('chest', 0.6), ('shoulder' + side, 0.4)], trunk_region)  # shoulder blade

	# ---- neck
	add(f_cone(NECK_A, NECK_B, 0.112, 0.060),
		[('chest', lambda t, P: 1 - ss(0.0, 0.25, t)), ('neck1', lambda t, P: ss(0.0, 0.25, t) * (1 - ss(0.4, 0.65, t))),
		 ('neck2', lambda t, P: ss(0.4, 0.65, t) * (1 - ss(0.85, 1.0, t))), ('head', lambda t, P: ss(0.85, 1.0, t))], neck_region)
	add(f_ell((0, 0.985, 0.455), (0.055, 0.150, 0.060), rot(0.45, 0, 0)), [('neck1', 0.5), ('neck2', 0.5)], 'coat')      # crest
	add(f_ell((0, 0.870, 0.470), (0.085, 0.125, 0.090), rot(0.5, 0, 0)), [('neck1', 0.8), ('chest', 0.2)], neck_region)  # base of the throat

	# ---- head
	add(f_ell((0, 1.255, 0.612), (0.060, 0.066, 0.082)), 'head', head_region)                    # skull
	add(f_ell((0, 1.290, 0.640), (0.047, 0.036, 0.070)), 'head', head_region)                    # forehead
	add(f_cone(MUZ_A, MUZ_B, 0.049, 0.029), 'head', muzzle_region)                               # muzzle
	add(f_cone((0, 1.205, 0.600), (0, 1.150, 0.790), 0.040, 0.022), 'head', jaw_region)          # lower jaw
	add(f_sph((0, 1.170, 0.842), 0.025), 'head', 'dark', True)                                   # nose
	for s in (-1, 1):
		add(f_ell((s * 0.043, 1.240, 0.650), (0.022, 0.032, 0.050)), 'head', 'coat', True)      # cheek
		add(f_sph(EYES[(s + 1) // 2], 0.0165), 'head', 'dark', True)                             # eye
		# ears: big, up and out and a little back, the opening facing forward and out; the inside pale
		side = 'L' if s > 0 else 'R'
		base = JP['ear' + side]; d = np.array((s * 0.55, 0.78, -0.30)); d /= np.linalg.norm(d)
		R = axes(d, (s * 0.35, 0.0, 1.0)); c = base + 0.078 * d
		face = R[:, 0]
		ear = (lambda c, face, R: (lambda P, t: np.where(((P - c) @ face > 0.006) & ((((P - c) @ R[:, 1]) / 0.068) ** 2 + (((P - c) @ R[:, 2]) / 0.032) ** 2 < 1), 'ear', 'coat')))(c, face, R)
		add(f_ell(c, (0.013, 0.082, 0.044), R), 'ear' + side, ear, True)

	# ---- front legs
	for side, s in (('L', 1), ('R', -1)):
		sh, el, ca, fe = (n + side for n in ('shoulder', 'elbow', 'carpus', 'fetlock'))
		S, E, C, F = JP[sh], JP[el], JP[ca], JP[fe]
		add(f_cone(S, E, 0.066, 0.044), chain('chest', sh, el, 0.3), 'coat')             # upper arm
		add(f_sph(E, 0.040), [(sh, 0.5), (el, 0.5)], 'coat')                             # elbow
		add(f_cone(E, C, 0.037, 0.022), chain(sh, el, ca), inner(s, E[0]))                       # forearm
		add(f_ell(E + 0.28 * (C - E) + np.array((0, 0, 0.008)), (0.034, 0.075, 0.038)), el, inner(s, E[0]))   # forearm muscle
		add(f_sph(C, 0.024), [(el, 0.5), (ca, 0.5)], 'coat')                                     # knee
		add(f_cone(C, F, 0.0185, 0.016), chain(el, ca, fe), 'coat')                              # cannon
		add(f_sph(F, 0.0205), [(ca, 0.5), (fe, 0.5)], 'coat')                                    # fetlock
		add(f_cone(F, F + np.array((0, -0.040, 0.016)), 0.018, 0.019), fe, hoof_region)          # pastern
		add(f_cone(F + np.array((0, -0.032, 0.008)), F + np.array((0, -0.066, 0.048)), 0.022, 0.016), fe, 'dark')  # hoof

	# ---- hind legs
	for side, s in (('L', 1), ('R', -1)):
		th, st, ho, fe = (n + side for n in ('thigh', 'stifle', 'hock', 'hfetlock'))
		T, St, H, F = JP[th], JP[st], JP[ho], JP[fe]
		add(f_cone(T, St, 0.088, 0.058), chain('hips', th, st, 0.3), 'coat')            # thigh
		add(f_sph(St, 0.050), [(th, 0.5), (st, 0.5)], 'coat')                           # stifle
		add(f_cone(St, H, 0.054, 0.026), chain(th, st, ho), inner(s, St[0]))                     # gaskin
		add(f_ell(St + 0.33 * (H - St) + np.array((0, 0, -0.012)), (0.040, 0.080, 0.050), rot(-0.9, 0, 0)), st, inner(s, St[0]))  # gaskin muscle
		add(f_sph(H, 0.026), [(st, 0.5), (ho, 0.5)], 'coat')                                     # hock
		add(f_sph(H + np.array((0, 0.012, -0.024)), 0.017), ho, 'coat')                          # point of the hock
		add(f_cone(H, F, 0.021, 0.016), chain(st, ho, fe), 'coat')                               # cannon
		add(f_sph(F, 0.0205), [(ho, 0.5), (fe, 0.5)], 'coat')                                    # fetlock
		add(f_cone(F, F + np.array((0, -0.040, 0.016)), 0.018, 0.019), fe, hoof_region)          # pastern
		add(f_cone(F + np.array((0, -0.032, 0.008)), F + np.array((0, -0.066, 0.048)), 0.022, 0.016), fe, 'dark')  # hoof

	# ---- tail: broad and flat, hanging over the rump; its underside (towards the body) white, shown when it is raised
	T1, T2 = JP['tail1'], JP['tail2']
	d = T1 - T2; R = axes(d, (0, 0.30, 1.0)); c = (T1 + T2) / 2 + np.array((0, -0.015, -0.006))
	under = R[:, 0]
	tw = lambda t, P: ss(-0.02, 0.06, (P - c) @ (-d / np.linalg.norm(d)))
	add(f_ell(c + 0.02 * (T2 - T1) / np.linalg.norm(T2 - T1), (0.024, 0.125, 0.066), R), [('tail1', lambda t, P: 1 - tw(t, P)), ('tail2', tw)],
		lambda P, t: np.where(((P - c) @ under > 0.008) | (np.abs(P[:, 0]) > 0.046), 'white', 'coat'))
	add(f_ell(T1 + np.array((0, -0.01, 0.01)), (0.040, 0.035, 0.035)), [('hips', 0.5), ('tail1', 0.5)], 'coat')   # root of the tail

def eval_all(P):
	D = np.empty((len(PRIMS), len(P))); T = np.empty_like(D)
	for i, (fn, _, _) in enumerate(PRIMS):
		D[i], T[i] = fn(P)
	return D, T

def sdf(P):
	D, _ = eval_all(P)
	det = np.zeros(len(PRIMS), bool); det[DETAIL] = True
	S = smin_exp(np.vstack([D[~det], smin_exp(D[det], K_DETAIL)[None]]), K_BLEND)
	return np.maximum(S, -P[:, 1])       # hooves flat on the ground

# ---------------------------------------------------------------- weights + regions
REGIONS = ['coat', 'ear', 'white', 'dark']
# Summer coat: reddish brown; the ears pale inside; white belly, throat, chin, eye ring, inner legs, tail underside.
PALETTE = dict(coat=0x9a6b42, ear=0xc4ab92, white=0xe8e2d6, dark=0x1e1814)
WINTER = dict(coat=0x76604c, ear=0xb3a493, white=0xe2ddd3, dark=0x1e1814)

def regions_at(P):
	D, T = eval_all(P)
	dom = D.argmin(0)
	reg = np.empty(len(P), dtype=object)
	for i, (_, _, region) in enumerate(PRIMS):
		m = dom == i
		if m.any(): reg[m] = region if isinstance(region, str) else region(P[m], T[i][m])
	return np.array([REGIONS.index(r) for r in reg])

def skin_weights(V):
	D, T = eval_all(V)
	dmin = D.min(0)
	C = np.exp(-(D - dmin) / 0.008)
	W = np.zeros((len(V), len(BONES)))
	for i, (_, bones, _) in enumerate(PRIMS):
		for name, w in bones:
			wv = w(T[i], V) if callable(w) else w
			W[:, BI[name]] += C[i] * np.clip(wv, 0, None)
	order = np.argsort(-W, axis=1)[:, :4]
	bw = np.take_along_axis(W, order, 1); bw /= bw.sum(1, keepdims=True)
	return order, bw

def importance(V):
	imp = np.ones(len(V))
	x, y, z = V[:, 0], V[:, 1], V[:, 2]
	imp[y < 0.55] = 2.5                                                    # the thin legs keep their roundness
	imp[y < 0.09] = 3.0                                                    # hooves
	imp[(z > 0.52) & (y > 1.10)] = 5.0                                     # head
	for e in ('earL', 'earR'): imp[np.linalg.norm(V - JP[e], axis=1) < 0.17] = 4.0
	imp[(z < -0.56) & (y > 0.68)] = np.maximum(imp[(z < -0.56) & (y > 0.68)], 2.5)   # tail
	return imp

# ---------------------------------------------------------------- poses (absolute local Euler rotations, XYZ)
POSES = {
	'stand': {},
	'alert': {'neck1': (-0.20, 0, 0), 'neck2': (-0.10, 0, 0), 'head': (-0.15, 0, 0), 'earL': (0, 0, -0.25), 'earR': (0, 0, 0.25),
			  'tail1': (0.35, 0, 0)},
	'graze': {'neck1': (1.55, 0, 0), 'neck2': (0.55, 0, 0), 'head': (0.35, 0, 0),
			  'shoulderL': (-0.10, 0, 0.04), 'shoulderR': (0.06, 0, -0.04), 'carpusL': (0.0, 0, 0), 'earL': (0.3, 0, 0.2), 'earR': (0.2, 0, -0.1)},
	'walk1': {'shoulderL': (-0.35, 0, 0), 'elbowL': (0, 0, 0), 'carpusL': (0.10, 0, 0), 'shoulderR': (0.30, 0, 0), 'carpusR': (0.55, 0, 0), 'fetlockR': (0.4, 0, 0),
			  'thighL': (0.30, 0, 0), 'stifleL': (-0.2, 0, 0), 'hockL': (0.3, 0, 0), 'thighR': (-0.35, 0, 0), 'stifleR': (0.2, 0, 0), 'hockR': (-0.25, 0, 0),
			  'neck1': (0.10, 0, 0), 'head': (0.05, 0, 0)},
	'gather': {'shoulderL': (0.55, 0, 0), 'carpusL': (1.3, 0, 0), 'fetlockL': (0.6, 0, 0), 'shoulderR': (0.45, 0, 0), 'carpusR': (1.2, 0, 0), 'fetlockR': (0.6, 0, 0),
			   'thighL': (-0.75, 0, 0), 'stifleL': (0.35, 0, 0), 'hockL': (-0.55, 0, 0), 'thighR': (-0.65, 0, 0), 'stifleR': (0.3, 0, 0), 'hockR': (-0.5, 0, 0),
			   'spine': (-0.10, 0, 0), 'chest': (-0.05, 0, 0), 'neck1': (0.25, 0, 0), 'head': (0.15, 0, 0), 'tail1': (2.3, 0, 0), 'tail2': (0.25, 0, 0),
			   'earL': (0.45, 0, 0), 'earR': (0.45, 0, 0)},
	'extend': {'shoulderL': (-1.05, 0, 0), 'carpusL': (0.1, 0, 0), 'shoulderR': (-0.95, 0, 0), 'carpusR': (0.15, 0, 0),
			   'thighL': (0.95, 0, 0), 'stifleL': (-0.2, 0, 0), 'hockL': (0.55, 0, 0), 'thighR': (0.85, 0, 0), 'stifleR': (-0.15, 0, 0), 'hockR': (0.5, 0, 0),
			   'spine': (0.06, 0, 0), 'neck1': (0.45, 0, 0), 'head': (0.2, 0, 0), 'tail1': (2.3, 0, 0), 'tail2': (0.25, 0, 0),
			   'earL': (0.5, 0, 0), 'earR': (0.5, 0, 0)},
	'flag': {'tail1': (2.3, 0, 0), 'tail2': (0.25, 0, 0), 'neck1': (-0.15, 0, 0), 'head': (-0.1, 0, 0)},
}

def build(target, h):
	build_prims()
	t0 = time.time()
	V, F = surface_nets(sdf, (-0.27, -0.012, -0.80), (0.27, 1.50, 0.92), h)
	print('surface nets:', len(V), 'verts', len(F), 'tris', round(time.time() - t0, 1), 's'); t0 = time.time()
	freg = regions_at(V[F].mean(1))
	V, F, freg = decimate(V, F, target, importance(V), freg)
	print('decimated:', len(V), 'verts', len(F), 'tris', round(time.time() - t0, 1), 's')
	N = vertex_normals(V, F)                       # smooth normals, computed before the split
	bi, bw = skin_weights(V)
	V, F, reg, src = split_by_region(V, F, freg)
	print('after region split:', len(V), 'verts')
	return dict(pos=V, nor=N[src], idx=F, bi=bi[src], bw=bw[src], reg=reg)

def export(mesh, path):
	r = lambda a, n: np.round(np.asarray(a, float), n).ravel().tolist()
	data = dict(
		bones=[dict(n=n, p=p, o=[round(v, 4) for v in o]) for n, p, o, _ in BONES],
		poses={k: {b: [round(x, 4) for x in e] for b, e in v.items()} for k, v in POSES.items()},
		regions=REGIONS, palette=PALETTE, winter=WINTER,
		pos=r(mesh['pos'], 4), nor=r(mesh['nor'], 3), reg=mesh['reg'].tolist(),
		bi=mesh['bi'].ravel().tolist(), bw=r(mesh['bw'], 3), idx=mesh['idx'].ravel().tolist())
	if 'uv' in mesh: data['uv'] = r(mesh['uv'], 5)
	json.dump(data, open(path, 'w'), separators=(',', ':'))

if __name__ == '__main__':
	os.chdir(os.path.dirname(os.path.abspath(__file__)))
	target = int(sys.argv[1]) if len(sys.argv) > 1 else 1600
	h = float(sys.argv[2]) if len(sys.argv) > 2 else 0.006
	mesh = build(target, h)
	np.savez('out/mesh.npz', **mesh)
	export(mesh, 'out/deer.json')
