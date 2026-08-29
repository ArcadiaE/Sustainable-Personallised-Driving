# -*- coding: utf-8 -*-
"""Generate candidate study routes from the road-network graph."""
import json, math, itertools

NET = json.load(open('network_unity.json', encoding='utf-8'))
SEGS = {s['id']: s for s in (NET['segments'] if isinstance(NET, dict) else NET)}
CAND = json.load(open('route_candidates.json', encoding='utf-8'))
OPTS = CAND if isinstance(CAND, list) else CAND.get('options', [])

TREE_C, TREE_R = (701.0, 397.0), 104.0

HALFW = {'primary': 4.0, 'secondary': 3.7, 'tertiary': 3.3}
def halfw(cls): return HALFW.get(cls, 2.8)

def seg_pts(sid, fwd=True):
    s = SEGS[sid]
    pts = list(zip(s['xs'], s['zs']))
    return pts if fwd else pts[::-1]

def length(pts):
    return sum(math.dist(pts[i], pts[i+1]) for i in range(len(pts)-1))

def bend_total(pts):
    tot = 0.0
    for i in range(1, len(pts)-1):
        a = math.degrees(math.atan2(pts[i][1]-pts[i-1][1], pts[i][0]-pts[i-1][0]))
        b = math.degrees(math.atan2(pts[i+1][1]-pts[i][1], pts[i+1][0]-pts[i][0]))
        d = abs(a-b) % 360
        tot += min(d, 360-d)
    return tot

def in_tree_zone(pts):
    return any(math.dist(p, TREE_C) < TREE_R for p in pts)

adj = {}
for sid, s in SEGS.items():
    adj.setdefault(s['startNode'], []).append((sid, True))
    adj.setdefault(s['endNode'], []).append((sid, False))
def other_end(sid, fwd):
    s = SEGS[sid]
    return s['endNode'] if fwd else s['startNode']

proposals = []

old = next(o for o in OPTS if o['startNode'] == 26592078)
new = next(o for o in OPTS if o['startNode'] == 26592053)
r_old = old['routes'][0]; r_new = new['routes'][0]
proposals.append(dict(label='R1', source='current set', streets=r_old['streets'],
                      lengthM=r_old['lengthM'], pts=r_old['pts'], halfw=r_old['halfw']))
proposals.append(dict(label='R2', source='Sloane set', streets=r_new['streets'],
                      lengthM=r_new['lengthM'], pts=r_new['pts'], halfw=r_new['halfw']))

LOOP_NAMES = {"King's Road", 'Sloane Square', 'Symons Street', 'Cadogan Gardens'}
loop_sids = [sid for sid, s in SEGS.items() if s.get('name') in LOOP_NAMES]
loop_adj = {}
for sid in loop_sids:
    s = SEGS[sid]
    loop_adj.setdefault(s['startNode'], []).append((sid, True))
    loop_adj.setdefault(s['endNode'], []).append((sid, False))
best_cycle = None
for start in loop_adj:
    # DFS with path
    stack = [(start, [], set())]
    while stack:
        node, path, used = stack.pop()
        if len(path) > 8: continue
        for sid, fwd in loop_adj.get(node, []):
            if sid in used: continue
            nxt = other_end(sid, fwd)
            npath = path + [(sid, fwd)]
            if nxt == start and len(npath) >= 3:
                pts = []
                for s2, f2 in npath:
                    p = seg_pts(s2, f2)
                    pts.extend(p if not pts else p[1:])
                L = length(pts)
                cx, cz = 765.0, 315.0
                inside = False
                j = len(pts) - 1
                for i in range(len(pts)):
                    if (pts[i][1] > cz) != (pts[j][1] > cz) and \
                       cx < (pts[j][0]-pts[i][0]) * (cz-pts[i][1]) / (pts[j][1]-pts[i][1]+1e-9) + pts[i][0]:
                        inside = not inside
                    j = i
                if inside and 250 <= L <= 700 and (best_cycle is None or L < best_cycle[0]):
                    best_cycle = (L, pts, npath)
            elif nxt not in [other_end(s3, f3) for s3, f3 in path[:-1]]:
                stack.append((nxt, npath, used | {sid}))
if best_cycle:
    L, pts, npath = best_cycle
    names = []
    for s2, _ in npath:
        n = SEGS[s2].get('name', '?')
        if not names or names[-1] != n: names.append(n)
    hw = []
    for s2, f2 in npath:
        p = seg_pts(s2, f2); h = halfw(SEGS[s2].get('cls', ''))
        hw.extend([h]*len(p) if not hw else [h]*(len(p)-1))
    proposals.append(dict(label='R3', source='store loop', streets=names,
                          lengthM=round(L, 1), pts=pts, halfw=hw))
    print(f"R3 loop: {L:.0f} m via {' -> '.join(names)}")
else:
    print('R3 loop: NOT FOUND')

scored = []
for start_node in adj:
    stack = [(start_node, [], 0.0)]
    while stack:
        node, path, dist = stack.pop()
        if dist > 235: continue
        if 150 <= dist <= 235 and len(path) >= 1:
            pts = []
            for s2, f2 in path:
                p = seg_pts(s2, f2)
                pts.extend(p if not pts else p[1:])
            if not in_tree_zone(pts):
                bend = bend_total(pts)
                names = []
                for s2, _ in path:
                    n = SEGS[s2].get('name', '?')
                    if not names or names[-1] != n: names.append(n)
                cls_ok = all(SEGS[s2].get('cls', '') in ('primary', 'secondary', 'tertiary', 'residential') for s2, _ in path)
                if cls_ok and 25 <= bend <= 200:
                    sc = min(bend, 120) / 30.0 + (1.0 if len(names) >= 2 else 0.0)
                    cx = sum(p[0] for p in pts)/len(pts); cz = sum(p[1] for p in pts)/len(pts)
                    scored.append((sc, dist, bend, names, pts, path, (cx, cz)))
            continue   # don't extend past a completed candidate to bound work
        if len(path) >= 6: continue
        for sid, fwd in adj.get(node, []):
            if any(sid == s3 for s3, _ in path): continue
            L = length(seg_pts(sid))
            stack.append((other_end(sid, fwd), path + [(sid, fwd)], dist + L))

def overlaps(pts, others, thresh=30):
    return any(min(math.dist(p, q) for q in o) < thresh for o in others for p in pts[::4])
chosen = []
taken_pts = [p['pts'] for p in proposals]
for sc, dist, bend, names, pts, path, (cx, cz) in sorted(scored, key=lambda t: -t[0]):
    bucket = (int(cx // 250), int(cz // 250))
    if any(b == bucket for _, b in chosen): continue
    if overlaps(pts, taken_pts): continue
    chosen.append(((sc, dist, bend, names, pts), bucket))
    taken_pts.append(pts)
    if len(chosen) == 4: break
for i, ((sc, dist, bend, names, pts), _) in enumerate(chosen):
    hw = [2.8]*len(pts)
    proposals.append(dict(label=f'R{4+i}', source='bendy candidate', streets=names,
                          lengthM=round(dist, 1), pts=[list(p) for p in pts], halfw=hw))
    print(f"R{4+i}: {dist:.0f} m bend={bend:.0f}° via {' -> '.join(names)}")

json.dump(proposals, open('route_proposals.json', 'w', encoding='utf-8'))
print(f"\n{len(proposals)} proposals -> route_proposals.json")
