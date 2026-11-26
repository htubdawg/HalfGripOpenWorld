import re, sys, glob, os, collections
# Pools trafficview reports: python3 pool.py <folder of s1.txt..s16.txt> [more folders]. See docs/HANDOFF.md.

def num(f):
    return int(re.findall(r'\d+', os.path.basename(f))[0])

for d in sys.argv[1:]:
    tot = 0; n = 0; p90 = []; p99 = []; o1 = 0; o3 = 0; fails = []; most = 0; med = []
    ways = collections.defaultdict(float); extra = []; cls = {}; ms = []
    for f in sorted(glob.glob(d + '/s*.txt'), key=num):
        t = open(f).read()
        m = re.search(r'fairness: (\d+) trips; stood still per trip mean ([\d.]+) s, median (\d+), 90% (\d+), 99% (\d+), most (\d+); over 1 min (\d+), over 3 min (\d+)', t)
        if not m:
            print(d, f, 'no fairness'); continue
        k = int(m[1]); n += k; tot += k * float(m[2]); med.append(int(m[3])); p90.append(int(m[4])); p99.append(int(m[5]))
        most = max(most, int(m[6])); o1 += int(m[7]); o3 += int(m[8])
        for l in t.splitlines():
            if 'FAIL' in l:
                fails.append(os.path.basename(f) + ': ' + l.strip()[:150])
        for c in ('Primary', 'Secondary', 'Street', 'Lane'):
            mm = re.search(r'\n    ' + c + r'\s+(\d+)\s+(\d+) to (\d+)', t)
            if mm:
                cls.setdefault(c, []).append(int(mm[1]))
        b = re.search(r'built in (\d+) ms', t)
        if b:
            ms.append(int(b[1]))
        r = re.search(r'routes: (.*)', t)
        if r:
            extra.append(r[1])
        for w in re.finditer(r'node (\d+) at \(([-\d]+), ([-\d]+)\) way in (\d+) .*?: ([\d.]+) min stood', t):
            ways[(int(w[1]), int(w[4]))] += float(w[5])
    a = lambda v: sum(v) / max(1, len(v))
    print(f'{d}: {n} trips, mean {tot/max(1,n):.1f} s, median {a(med):.1f}, 90% {a(p90):.0f}, 99% {a(p99):.0f}, most {most}, over1 {o1}, over3 {o3}, FAILs {len(fails)}; built in {a(ms):.0f} ms')
    for x in fails:
        print('   ', x)
    print('    top ways (min summed from the top-10 lists):', ', '.join(f'{k[0]}/{k[1]} {v:.0f}' for k, v in sorted(ways.items(), key=lambda kv: -kv[1])[:10]))
    print('    AADT medians averaged:', ', '.join(f'{c} {a(v):.0f}' for c, v in cls.items()))
    rr = [re.search(r'(\d+) trips chose.*?, (\d+) of them', e) for e in extra]
    rr = [(int(x[1]), int(x[2])) for x in rr if x]
    if rr:
        print('    rerouted', sum(x[0] for x in rr), 'changed', sum(x[1] for x in rr))
