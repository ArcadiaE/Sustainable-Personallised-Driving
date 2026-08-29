"""Export the drivable road network as a JSON graph."""
import json, math, os, struct, sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "EcoHUD", "tools"))
from route_centerline_snap import parse_db, decode_nodes, latlon_to_xz, DRIVABLE

FOOT = {"footway", "path", "pedestrian", "steps", "living_street"}
OUT  = os.path.join(os.path.dirname(os.path.abspath(__file__)), "network.json")
PNG  = os.path.join(os.path.dirname(os.path.abspath(__file__)), "network_preview.png")

LOOP_CX, LOOP_CZ, LOOP_R = 600.0, 225.0, 320.0

def build(kind_filter):
    origin, nodes, ways = parse_db()
    olat, olon = origin["lat"], origin["lon"]
    nxz = {i: latlon_to_xz(la, lo, olat, olon) for i, (la, lo) in nodes.items()}

    kept = []
    for w in ways:
        hw = w["tags"].get("highway")
        if hw not in kind_filter:
            continue
        ids = [i for i in decode_nodes(w["hex"]) if i in nxz]
        if len(ids) >= 2:
            kept.append({"ids": ids, "tags": w["tags"], "wid": w["id"]})

    deg = {}
    for w in kept:
        ids = w["ids"]
        for k, nid in enumerate(ids):
            deg[nid] = deg.get(nid, 0) + (1 if k in (0, len(ids) - 1) else 2)

    segments = []
    for w in kept:
        ids = w["ids"]
        cur = [ids[0]]
        for nid in ids[1:]:
            cur.append(nid)
            if deg.get(nid, 0) >= 3 and nid != ids[-1]:
                segments.append({"ids": cur, "tags": w["tags"], "wid": w["wid"]})
                cur = [nid]
        if len(cur) >= 2:
            segments.append({"ids": cur, "tags": w["tags"], "wid": w["wid"]})

    junctions = {nid: d for nid, d in deg.items() if d >= 3}

    def seg_json(s, idx):
        t = s["tags"]
        pts = [[round(nxz[i][0], 2), round(nxz[i][1], 2)] for i in s["ids"]]
        length = sum(math.dist(pts[k], pts[k + 1]) for k in range(len(pts) - 1))
        return {
            "id": idx, "way": s["wid"],
            "name": t.get("name", ""),
            "class": t.get("highway", ""),
            "oneway": t.get("oneway", "no"),
            "maxspeed": t.get("maxspeed", ""),
            "startNode": s["ids"][0], "endNode": s["ids"][-1],
            "lengthM": round(length, 1),
            "pts": pts,
        }

    seg_list = [seg_json(s, i) for i, s in enumerate(segments)]
    junc_list = [
        {"node": nid, "x": round(nxz[nid][0], 2), "z": round(nxz[nid][1], 2), "arms": d}
        for nid, d in sorted(junctions.items())
    ]
    return origin, seg_list, junc_list

def largest_scc(segs):
    """Directed graph over junction nodes: two-way segment = edges both ways,
    oneway = one edge ('-1' = reversed). Returns the node set of the largest
    strongly connected component, so every kept lane always has a way out
    (a trapped vehicle blocks a street and cascades into gridlock)."""
    adj = {}
    def add(a, b):
        adj.setdefault(a, []).append(b)
        adj.setdefault(b, [])
    for s in segs:
        a, b, ow = s["startNode"], s["endNode"], s["oneway"]
        if ow in ("yes", "true", "1"):
            add(a, b)
        elif ow == "-1":
            add(b, a)
        else:
            add(a, b); add(b, a)

    # iterative Tarjan
    index = {}; low = {}; onstk = {}; stk = []; sccs = []; counter = [0]
    for root in adj:
        if root in index: continue
        work = [(root, iter(adj[root]))]
        index[root] = low[root] = counter[0]; counter[0] += 1
        stk.append(root); onstk[root] = True
        while work:
            v, it = work[-1]
            advanced = False
            for w in it:
                if w not in index:
                    index[w] = low[w] = counter[0]; counter[0] += 1
                    stk.append(w); onstk[w] = True
                    work.append((w, iter(adj[w])))
                    advanced = True
                    break
                elif onstk.get(w):
                    low[v] = min(low[v], index[w])
            if advanced: continue
            work.pop()
            if work:
                pv = work[-1][0]
                low[pv] = min(low[pv], low[v])
            if low[v] == index[v]:
                comp = []
                while True:
                    w = stk.pop(); onstk[w] = False; comp.append(w)
                    if w == v: break
                sccs.append(comp)
    return set(max(sccs, key=len)) if sccs else set()

AREAS = [(606.0, 228.0, 500.0), (490.0, 1070.0, 420.0)]

def dist_to_areas(p):
    """Positive = outside all circles; <=0 inside. Returns min over circles of
    (distance-to-centre - radius), so callers can test margins uniformly."""
    return min(math.dist(p, (cx, cz)) - r for cx, cz, r in AREAS)

TRAFFIC_CLASSES = DRIVABLE - {"service"}

def prune_interior_leaves(segs):
    """Iteratively removes dead-end chains whose tip lies INSIDE the area
    (true culs-de-sac: ambient cars have no business entering them). Stubs cut
    by the area boundary are kept - traffic U-turns at the area edge there."""
    removed = 0
    while True:
        deg = {}
        for s in segs:
            deg[s["startNode"]] = deg.get(s["startNode"], 0) + 1
            deg[s["endNode"]] = deg.get(s["endNode"], 0) + 1
        def interior_tip(s, node_key, pt):
            return deg[s[node_key]] == 1 and dist_to_areas(pt) < -40.0
        keep = []
        for s in segs:
            if interior_tip(s, "startNode", tuple(s["pts"][0])) or \
               interior_tip(s, "endNode", tuple(s["pts"][-1])):
                removed += 1
            else:
                keep.append(s)
        if len(keep) == len(segs):
            return keep, removed
        segs = keep

def in_area(s):
    return any(dist_to_areas((p[0], p[1])) <= 0.0 for p in s["pts"])

def normalize_oneway(segs):
    """OSM oneway='-1' means 'against way direction': flip the polyline once so
    all oneways flow start->end and downstream logic has a single convention."""
    for s in segs:
        if s["oneway"] == "-1":
            s["pts"].reverse()
            s["startNode"], s["endNode"] = s["endNode"], s["startNode"]
            s["oneway"] = "yes"

def merge_way_joints(segs, junction_nodes):
    """OSM splits one street into several ways (speed-limit changes etc.), so
    segments meet at degree-2 'joint' nodes that are NOT junctions. The Unity
    builder only creates lane connections at junctions, which stranded vehicles
    at every joint (trap waypoints). Merge compatible pairs through each joint
    until every segment endpoint is a real junction or a true dead end."""
    def flip(s):
        s["pts"].reverse()
        s["startNode"], s["endNode"] = s["endNode"], s["startNode"]

    merged_count = 0
    changed = True
    while changed:
        changed = False
        inc = {}
        for s in segs:
            inc.setdefault(s["startNode"], []).append((s, "s"))
            inc.setdefault(s["endNode"], []).append((s, "e"))
        for node, lst in inc.items():
            if node in junction_nodes or len(lst) != 2:
                continue
            (a, ea), (b, eb) = lst
            if a is b:
                continue
            two_a, two_b = a["oneway"] in ("", "no"), b["oneway"] in ("", "no")
            if two_a and two_b:
                if ea == "s": flip(a)
                if eb == "e": flip(b)
            elif not two_a and not two_b:
                if ea == "s" and eb == "e":
                    a, b = b, a
                elif not (ea == "e" and eb == "s"):
                    continue
            else:
                continue
            a["pts"] = a["pts"] + b["pts"][1:]
            a["endNode"] = b["endNode"]
            a["lengthM"] = round(a["lengthM"] + b["lengthM"], 1)
            segs.remove(b)
            merged_count += 1
            changed = True
            break
    return segs, merged_count

def main():
    origin, drive_segs, drive_juncs = build(TRAFFIC_CLASSES)
    _, foot_segs, _ = build(FOOT)

    before_area = len(drive_segs)
    drive_segs = [s for s in drive_segs if in_area(s)]
    print(f"area cut: kept {len(drive_segs)}/{before_area} segments "
          f"(areas {AREAS}, no service roads)")

    scc = largest_scc(drive_segs)
    before = len(drive_segs)
    drive_segs = [s for s in drive_segs if s["startNode"] in scc and s["endNode"] in scc]
    print(f"SCC prune: kept {len(drive_segs)}/{before} segments "
          f"({before - len(drive_segs)} trap/unreachable removed)")

    drive_segs, leaves = prune_interior_leaves(drive_segs)
    print(f"leaf prune: {leaves} interior dead-end segments removed, {len(drive_segs)} remain")

    normalize_oneway(drive_segs)
    junction_nodes = {j["node"] for j in drive_juncs}
    drive_segs, merged = merge_way_joints(drive_segs, junction_nodes)
    print(f"joint merge: {merged} way joints merged, {len(drive_segs)} segments remain")
    left = sum(1 for s in drive_segs
               for key, idx in (("startNode", 0), ("endNode", -1))
               if s[key] not in junction_nodes)
    print(f"non-junction endpoints remaining (dead ends / unmergeable joints): {left}")

    data = {
        "origin": origin,
        "drivable": {"segments": drive_segs, "junctions": drive_juncs},
        "foot": {"segments": foot_segs},
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)

    unity = {
        "segments": [
            {
                "id": s["id"], "name": s["name"], "cls": s["class"],
                "oneway": s["oneway"], "maxspeed": s["maxspeed"],
                "startNode": s["startNode"], "endNode": s["endNode"],
                "lengthM": s["lengthM"],
                "xs": [p[0] for p in s["pts"]],
                "zs": [p[1] for p in s["pts"]],
            }
            for s in drive_segs
        ],
        "junctions": drive_juncs,
    }
    unity_out = OUT.replace("network.json", "network_unity.json")
    with open(unity_out, "w", encoding="utf-8") as f:
        json.dump(unity, f, ensure_ascii=False)
    print("wrote", unity_out)

    km = sum(s["lengthM"] for s in drive_segs) / 1000.0
    fkm = sum(s["lengthM"] for s in foot_segs) / 1000.0
    xs = [p[0] for s in drive_segs for p in s["pts"]]
    zs = [p[1] for s in drive_segs for p in s["pts"]]
    print(f"drivable: {len(drive_segs)} segments, {len(drive_juncs)} junctions, {km:.1f} km")
    print(f"foot:     {len(foot_segs)} segments, {fkm:.1f} km")
    print(f"bbox: x [{min(xs):.0f}, {max(xs):.0f}]  z [{min(zs):.0f}, {max(zs):.0f}]")
    print("wrote", OUT)

    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(figsize=(14, 14))
        for s in foot_segs:
            p = s["pts"]
            ax.plot([q[0] for q in p], [q[1] for q in p], color="#7bc47b", lw=0.5, zorder=1)
        for s in drive_segs:
            p = s["pts"]
            ax.plot([q[0] for q in p], [q[1] for q in p], color="#222", lw=1.1, zorder=2)
        ax.scatter([j["x"] for j in drive_juncs], [j["z"] for j in drive_juncs],
                   s=6, c="red", zorder=3)
        ax.add_patch(plt.Circle((LOOP_CX, LOOP_CZ), LOOP_R, fill=False,
                                color="#2f6fa7", lw=2, ls="--", zorder=4))
        ax.set_aspect("equal"); ax.set_title("Study district network (black=drivable, green=foot, red=junctions)")
        fig.savefig(PNG, dpi=140, bbox_inches="tight")
        print("wrote", PNG)
    except ImportError:
        print("matplotlib not available - skipped preview image")

if __name__ == "__main__":
    main()
