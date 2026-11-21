"""Shared mesh tools, copied from htubdawg/Claudeinoid (sculpt/sculpt.py): SDF primitives, smooth min/max, surface nets,
region-keeping QEM decimation, normals and the region split. Generic: nothing here knows what is being sculpted."""
import numpy as np, math, heapq

def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

def f_ell(c, r, R=None):
    c = np.array(c, float); r = np.array(r, float); R = np.eye(3) if R is None else R
    def f(P):
        q = (P - c) @ R
        k0 = np.linalg.norm(q / r, axis=1); k1 = np.linalg.norm(q / (r * r), axis=1)
        return k0 * (k0 - 1) / np.maximum(k1, 1e-9), np.zeros(len(P))
    return f

def f_cone(a, b, ra, rb):
    a = np.array(a, float); b = np.array(b, float); ba = b - a; L2 = ba @ ba
    def f(P):
        t = np.clip(((P - a) @ ba) / L2, 0, 1)
        return np.linalg.norm(P - a - np.outer(t, ba), axis=1) - (ra + (rb - ra) * t), t
    return f

def f_sph(c, r):
    c = np.array(c, float)
    return lambda P: (np.linalg.norm(P - c, axis=1) - r, np.zeros(len(P)))


def smin_exp(D, k):
    m = D.min(0)
    return m - k * np.log(np.exp(-(D - m) / k).sum(0))

def smax_poly(a, b, k):
    h = np.clip(0.5 - 0.5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h + k * h * (1 - h)


def surface_nets(sdf, lo, hi, h):
    xs, ys, zs = [np.arange(lo[i], hi[i] + h, h) for i in range(3)]
    F = np.empty((len(xs), len(ys), len(zs)))
    X, Z = np.meshgrid(xs, zs, indexing='ij')
    for j, y in enumerate(ys):
        P = np.c_[X.ravel(), np.full(X.size, y), Z.ravel()]
        F[:, j, :] = sdf(P).reshape(X.shape)
    inside = F < 0
    nx, ny, nz = F.shape
    corners = [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    cnt = np.zeros((nx - 1, ny - 1, nz - 1), np.int8)
    for (i, j, k) in corners:
        cnt += inside[i:nx - 1 + i, j:ny - 1 + j, k:nz - 1 + k]
    active = (cnt > 0) & (cnt < 8)
    ai = np.argwhere(active)
    vid = -np.ones(active.shape, np.int64); vid[active] = np.arange(len(ai))
    acc = np.zeros((len(ai), 3)); n = np.zeros(len(ai))
    I, J, K = ai.T
    for a in range(8):
        for b in range(a + 1, 8):
            ca, cb = np.array(corners[a]), np.array(corners[b])
            if np.abs(ca - cb).sum() != 1: continue
            fa = F[I + ca[0], J + ca[1], K + ca[2]]; fb = F[I + cb[0], J + cb[1], K + cb[2]]
            m = (fa < 0) != (fb < 0)
            t = fa[m] / (fa[m] - fb[m])
            acc[m] += ca + t[:, None] * (cb - ca); n[m] += 1
    V = np.array(lo) + (ai + acc / n[:, None]) * h
    tris = []
    for ax in range(3):
        o1, o2 = [a for a in range(3) if a != ax]
        s0 = [slice(None)] * 3; s1 = [slice(None)] * 3
        s0[ax] = slice(0, -1); s1[ax] = slice(1, None)
        a_in = inside[tuple(s0)]; ch = a_in != inside[tuple(s1)]
        pts = np.argwhere(ch)
        ok = (pts[:, o1] >= 1) & (pts[:, o1] <= F.shape[o1] - 2) & (pts[:, o2] >= 1) & (pts[:, o2] <= F.shape[o2] - 2)
        pts = pts[ok]; inn = a_in[tuple(pts.T)]
        def cell(d1, d2):
            q = pts.copy(); q[:, o1] -= d1; q[:, o2] -= d2; return vid[tuple(q.T)]
        q0, q1, q2, q3 = cell(1, 1), cell(0, 1), cell(0, 0), cell(1, 0)
        t1 = np.c_[q0, q1, q2]; t2 = np.c_[q0, q2, q3]
        for t in (t1, t2):
            nrm = np.cross(V[t[:, 1]] - V[t[:, 0]], V[t[:, 2]] - V[t[:, 0]])
            want = np.where(inn, 1.0, -1.0)
            flip = nrm[:, ax] * want < 0
            t[flip] = t[flip][:, ::-1]
            tris.append(t)
    return V, np.vstack(tris)


def decimate(V, F, target, imp, freg, BW=8.0):
    """QEM decimation that keeps region borders: each triangle carries a region label, and
    border edges add constraint planes (perpendicular to the surface) so the clothing / eye /
    lip outlines stay as clean polylines. Returns V, F and the per-face regions."""
    V = V.copy(); n = len(V)
    Fa = np.array(F)
    P0 = V[Fa]; nrm = np.cross(P0[:, 1] - P0[:, 0], P0[:, 2] - P0[:, 0])
    ar = np.linalg.norm(nrm, axis=1); nn = nrm / np.maximum(ar, 1e-15)[:, None]
    pl = np.c_[nn, -(nn * P0[:, 0]).sum(1)]
    Kq = pl[:, :, None] * pl[:, None, :] * (0.5 * ar * imp[Fa].mean(1))[:, None, None]
    Q = np.zeros((n, 4, 4))
    for j in range(3): np.add.at(Q, Fa[:, j], Kq)
    # border constraint planes
    E = np.r_[Fa[:, [0, 1]], Fa[:, [1, 2]], Fa[:, [2, 0]]]; fid = np.tile(np.arange(len(Fa)), 3)
    Es = np.sort(E, 1); order = np.lexsort((Es[:, 1], Es[:, 0])); Es, fid = Es[order], fid[order]
    same = (Es[1:] == Es[:-1]).all(1)
    i0 = np.where(same)[0]; fa_, fb_ = fid[i0], fid[i0 + 1]
    brd = freg[fa_] != freg[fb_]
    ea, eb = Es[i0[brd], 0], Es[i0[brd], 1]
    en = nn[fa_[brd]] + nn[fb_[brd]]
    ed = V[eb] - V[ea]
    cn = np.cross(ed, en); cl = np.linalg.norm(cn, axis=1); ok_ = cl > 1e-15
    cn = cn[ok_] / cl[ok_, None]; ea, eb, ed = ea[ok_], eb[ok_], ed[ok_]
    cpl = np.c_[cn, -(cn * V[ea]).sum(1)]
    w = BW * (ed * ed).sum(1) * 0.5 * (imp[ea] + imp[eb])
    Kb = cpl[:, :, None] * cpl[:, None, :] * w[:, None, None]
    np.add.at(Q, ea, Kb); np.add.at(Q, eb, Kb)
    freg = list(map(int, freg))
    F = [list(map(int, f)) for f in Fa]
    vf = [set() for _ in range(n)]
    for fi, f in enumerate(F):
        for v in f: vf[v].add(fi)
    ver = [0] * n; alive = [True] * n

    def best(u, v):
        q = Q[u] + Q[v]; pu, pv = V[u], V[v]; mid = (pu + pv) / 2
        cands = [pu, pv, mid]
        A = q[:3, :3]
        if abs(np.linalg.det(A)) > 1e-18:
            x = np.linalg.solve(A, -q[:3, 3])
            if np.linalg.norm(x - mid) < np.linalg.norm(pu - pv): cands.append(x)
        bc, bx = None, None
        for x in cands:
            hx = np.append(x, 1.0); c = hx @ q @ hx
            if bc is None or c < bc: bc, bx = c, x
        return bc, bx

    heap = []
    edges = set()
    for f in F:
        for a, b in ((f[0], f[1]), (f[1], f[2]), (f[2], f[0])):
            edges.add((min(a, b), max(a, b)))
    for u, v in edges:
        c, x = best(u, v); heap.append((c, u, v, 0, 0, x))
    heapq.heapify(heap)
    nf = len(F)
    while nf > target and heap:
        c, u, v, vu, vv, x = heapq.heappop(heap)
        if not (alive[u] and alive[v]) or ver[u] != vu or ver[v] != vv: continue
        fu, fv = vf[u], vf[v]; shared = fu & fv
        if len(shared) != 2: continue
        Nu = {w for f in fu for w in F[f]} - {u}; Nv = {w for f in fv for w in F[f]} - {v}
        if len(Nu & Nv) != 2: continue
        bu = len({freg[f] for f in fu}) > 1; bv = len({freg[f] for f in fv}) > 1
        sa, sb = tuple(shared)
        if bu and bv and freg[sa] == freg[sb]: continue      # would pinch a region
        ok = True
        for f in (fu | fv) - shared:
            tri = F[f]
            o = [V[w] for w in tri]; nw = [x if w in (u, v) else V[w] for w in tri]
            n0 = np.cross(o[1] - o[0], o[2] - o[0]); n1 = np.cross(nw[1] - nw[0], nw[2] - nw[0])
            l0, l1 = np.linalg.norm(n0), np.linalg.norm(n1)
            if l1 < 1e-12 or n0 @ n1 < 0.35 * l0 * l1: ok = False; break
        if not ok: continue
        for f in shared:
            for w in F[f]: vf[w].discard(f)
            F[f] = None
        for f in vf[v]:
            F[f] = [u if w == v else w for w in F[f]]; vf[u].add(f)
        vf[v] = set(); alive[v] = False
        V[u] = x; Q[u] += Q[v]; ver[u] += 1
        nf -= 2
        for w in {w for f in vf[u] for w in F[f]} - {u}:
            c2, x2 = best(u, w); heapq.heappush(heap, (c2, u, w, ver[u], ver[w], x2))
    kid = [i for i, f in enumerate(F) if f is not None]
    keep = [F[i] for i in kid]
    used = sorted({w for f in keep for w in f})
    remap = {o: i for i, o in enumerate(used)}
    return V[used], np.array([[remap[w] for w in f] for f in keep]), np.array([freg[i] for i in kid])

def vertex_normals(V, F):
    n = np.zeros_like(V)
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
    for j in range(3): np.add.at(n, F[:, j], fn)
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


def split_by_region(V, F, freg):
    """Duplicate vertices along region borders so each triangle is a single flat colour.
    Returns new positions, faces, per-vertex region and the source index of each vertex."""
    key = {}; src = []; reg = []; F2 = np.empty_like(F)
    for fi, f in enumerate(F):
        r = int(freg[fi])
        for j, v in enumerate(f):
            k = (int(v), r)
            if k not in key: key[k] = len(src); src.append(int(v)); reg.append(r)
            F2[fi, j] = key[k]
    src = np.array(src)
    return V[src], F2, np.array(reg), src

