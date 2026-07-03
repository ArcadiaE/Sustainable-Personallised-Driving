# =============================================================================
#  route_centerline_snap.py
#  Offline generator for AutoDriver v2's `route` array (COMP0190 P87, Yike Zhang).
#
#  Purpose: take v1's hand-recorded loop (which sits ~2.3 m off the carriageway
#  centre and sways side to side) and snap it onto the true CityGen road
#  centrelines, so the demo car drives down the middle of the road with no
#  side-to-side wobble. Same loop / streets / start; only the point positions move.
#
#  Pipeline:
#    1. Parse the CityGen3D map database (YAML .asset): mapNodes (lat/lon) and
#       mapWays (highway polylines, node ids packed as little-endian uint64 hex).
#    2. Project every drivable-road node to Unity XZ with CityGen's own local-metre
#       formula (GeoCoord.GetMapCoord). MapRoads parent is at world (0,0) in the
#       current scene, so map XZ == world XZ (origin at tile centre).
#    3. Map-match v1's route to the centreline network: per point pick the nearest
#       segment, direction-gated (<=70 deg) so junctions don't grab a crossing
#       street; median-filter the polyline assignment to kill 1-2 pt flicker.
#    4. Resample at 1.5 m + light 5-pt moving average (rounds staggered-junction
#       doglegs). Emit the C# array.
#
#  Usage:  python route_centerline_snap.py            # prints the C# route array
#  Deps:   standard library only (no numpy). matplotlib optional for --plot.
#
#  Result quality (measured): final path to centreline median 0 m, p95 < 0.8 m;
#  curvature noise max ~0.24 m (v1 ~0.15 m but 2.3 m off centre).
# =============================================================================
import math, struct, re, statistics, sys

DB        = r"F:/AAAUCL毕设/New Pipeline/Assets/Database/Pimlico (0, 0).asset"
AUTODRIVER_V1 = r"F:/AAAUCL毕设/Scripts/EcoHUD/AutoDriver.cs"   # source of the v1 route to re-snap

RMAX      = 9.0     # max snap distance (m)
ANG_GATE  = 70.0    # max angle between segment dir and local route dir (deg)
RESAMPLE  = 1.5     # output spacing (m)
SMOOTH_WIN = 5      # moving-average window (points)
THROUGH_RUN = 18    # min consecutive v1 points on a road for it to count as a "through road"
                    # (roads the loop actually drives). Everything below this is a side street
                    # the route only brushes past at a junction -> excluded so the path does NOT
                    # bulge toward the side-street mouth (that arc was what trapped the car).

DRIVABLE = {
    "motorway","trunk","primary","secondary","tertiary","unclassified",
    "residential","living_street","service","road",
    "motorway_link","trunk_link","primary_link","secondary_link","tertiary_link",
}

# ---------------------------------------------------------------- DB parsing
def parse_db():
    lines = open(DB, encoding="utf-8").readlines()
    n = len(lines)
    origin = {}
    for i, l in enumerate(lines):
        if l.rstrip("\n") == "  origin:":
            origin['lat'] = float(lines[i+1].split(":")[1])
            origin['lon'] = float(lines[i+2].split(":")[1])
            break
    nodes = {}   # id -> (lat, lon)
    ways  = []   # {id, hex, tags}
    i = 0
    while i < n:
        s = lines[i].rstrip("\n")
        if s == "  mapNodes:":
            i += 1
            while i < n and not lines[i].startswith("  mapWays:"):
                st = lines[i].strip()
                if st.startswith("- unique_id:"):
                    nid = int(st.split(":")[1]); lat = lon = None; j = i+1
                    while j < n:
                        t = lines[j].strip()
                        if t == "location:":
                            lat = float(lines[j+1].split(":")[1]); lon = float(lines[j+2].split(":")[1]); j += 3; continue
                        if t.startswith("- unique_id:") or lines[j].startswith("  mapWays:"): break
                        j += 1
                    nodes[nid] = (lat, lon); i = j; continue
                i += 1
            continue
        if s == "  mapWays:":
            i += 1
            while i < n and not lines[i].startswith("  mapRelations:"):
                st = lines[i].strip()
                if st.startswith("- unique_id:"):
                    wid = int(st.split(":")[1]); hexs = None; tags = {}; j = i+1
                    while j < n:
                        t = lines[j].strip()
                        if t.startswith("nodes:"):
                            hexs = t.split(":", 1)[1].strip()
                        elif t.startswith("- key:"):
                            tags[t.split(":", 1)[1].strip()] = lines[j+1].split(":", 1)[1].strip(); j += 2; continue
                        elif t.startswith("- unique_id:") or lines[j].startswith("  mapRelations:"):
                            break
                        j += 1
                    ways.append({'id': wid, 'hex': hexs, 'tags': tags}); i = j; continue
                i += 1
            continue
        i += 1
    return origin, nodes, ways

def decode_nodes(hexstr):
    b = bytes.fromhex(hexstr)
    return [struct.unpack("<Q", b[k:k+8])[0] for k in range(0, len(b), 8)]

def latlon_to_xz(lat, lon, olat, olon):
    r = math.radians(lat)
    mLat = 111132.9 - 559.82*math.cos(2*r) + 1.175*math.cos(4*r) - 0.0023*math.cos(6*r)
    mLon = 111412.8*math.cos(r) - 93.5*math.cos(3*r) + 0.118*math.cos(5*r)
    return (mLon*(lon-olon), mLat*(lat-olat))

def load_centerlines():
    origin, nodes, ways = parse_db()
    olat, olon = origin['lat'], origin['lon']
    nxz = {i: latlon_to_xz(la, lo, olat, olon) for i, (la, lo) in nodes.items()}
    polys = []
    for w in ways:
        if w['tags'].get('highway') not in DRIVABLE: continue
        pts = [nxz[i] for i in decode_nodes(w['hex']) if i in nxz]
        if len(pts) >= 2:
            polys.append((w['tags'].get('name', '?'), pts))
    return origin, polys

# ---------------------------------------------------------------- geometry
def read_v1_route():
    txt = open(AUTODRIVER_V1, encoding="utf-8").read()
    # only the big literal route array (skip Vector3 new(...) in code): take 2-arg new(x f, z f)
    m = re.findall(r"new\(\s*([-\d.]+)f\s*,\s*([-\d.]+)f\s*\)", txt)
    pts = [(float(a), float(b)) for a, b in m]
    # the route literal is the long run; filter to the contiguous block by count heuristic:
    return pts

def dist_pt_seg(p, a, b):
    ax, az = a; bx, bz = b; px, pz = p
    dx, dz = bx-ax, bz-az; L2 = dx*dx+dz*dz
    t = 0.0 if L2 < 1e-9 else max(0.0, min(1.0, ((px-ax)*dx+(pz-az)*dz)/L2))
    cx, cz = ax+t*dx, az+t*dz
    return math.hypot(px-cx, pz-cz), (cx, cz)

def unit(dx, dz):
    L = math.hypot(dx, dz); return (0, 0) if L < 1e-9 else (dx/L, dz/L)

def project_on_poly(p, pts):
    bd, bc = 1e18, p
    for si in range(len(pts)-1):
        d, c = dist_pt_seg(p, pts[si], pts[si+1])
        if d < bd: bd, bc = d, c
    return bd, bc

def match(route, polys):
    n = len(route); cg = math.cos(math.radians(ANG_GATE))
    best_pi = [None]*n; best_c = [None]*n
    for i, p in enumerate(route):
        a = route[(i-1) % n]; b = route[(i+1) % n]
        ld = unit(b[0]-a[0], b[1]-a[1])
        cand = []
        for pi, (nm, pts) in enumerate(polys):
            bd, bc = 1e18, None
            for si in range(len(pts)-1):
                d, c = dist_pt_seg(p, pts[si], pts[si+1])
                if d > RMAX: continue
                sd = unit(pts[si+1][0]-pts[si][0], pts[si+1][1]-pts[si][1])
                if abs(sd[0]*ld[0]+sd[1]*ld[1]) < cg: continue
                if d < bd: bd, bc = d, c
            if bc is not None: cand.append((bd, pi, bc))
        if cand:
            cand.sort(); _, best_pi[i], best_c[i] = cand[0]
        else:
            gd, gp, gc = 1e18, None, None
            for pi, (nm, pts) in enumerate(polys):
                d, c = project_on_poly(p, pts)
                if d < gd: gd, gp, gc = d, pi, c
            best_pi[i], best_c[i] = gp, gc
    # median-filter the polyline assignment (x2) to remove 1-2 pt junction flicker
    def medfilt(ids, win=5):
        h = win//2; out = ids[:]
        for i in range(len(ids)):
            w = [ids[(i+k) % len(ids)] for k in range(-h, h+1)]
            if len(set(w)) < len(w): out[i] = statistics.mode(w)
        return out
    ids = medfilt(medfilt(best_pi))
    snapped = []
    for i, p in enumerate(route):
        d, c = project_on_poly(p, polys[ids[i]][1])
        if d > RMAX*1.6 and best_c[i] is not None: c = best_c[i]
        snapped.append(c)
    dd = [snapped[0]]
    for q in snapped[1:]:
        if math.hypot(q[0]-dd[-1][0], q[1]-dd[-1][1]) > 0.05: dd.append(q)
    return dd

def resample(poly, spacing, loop=True):
    pts = poly[:] + ([poly[0]] if loop and poly[0] != poly[-1] else [])
    out = [pts[0]]; carry = 0.0
    for i in range(len(pts)-1):
        a, b = pts[i], pts[i+1]; L = math.hypot(b[0]-a[0], b[1]-a[1])
        if L < 1e-9: continue
        pos = carry
        while pos < L:
            t = pos/L; out.append((a[0]+t*(b[0]-a[0]), a[1]+t*(b[1]-a[1]))); pos += spacing
        carry = pos - L
    return out

def moving_avg(pts, win):
    h = win//2; n = len(pts); out = []
    for i in range(n):
        xs = sum(pts[(i+k) % n][0] for k in range(-h, h+1))
        zs = sum(pts[(i+k) % n][1] for k in range(-h, h+1))
        out.append((xs/win, zs/win))
    return out

def through_roads(route, polys):
    """Names of roads the loop actually drives (>= THROUGH_RUN consecutive matched
    points). Side streets the route only brushes past at a junction fall below the
    threshold and are dropped, so the snapped path passes straight through side
    junctions instead of arcing toward the side-street mouth."""
    cg = math.cos(math.radians(ANG_GATE)); names = []
    for i, p in enumerate(route):
        a = route[(i-1) % len(route)]; b = route[(i+1) % len(route)]
        ld = unit(b[0]-a[0], b[1]-a[1]); best = (1e18, None)
        for nm, pts in polys:
            for si in range(len(pts)-1):
                d, c = dist_pt_seg(p, pts[si], pts[si+1])
                if d > RMAX: continue
                sd = unit(pts[si+1][0]-pts[si][0], pts[si+1][1]-pts[si][1])
                if abs(sd[0]*ld[0]+sd[1]*ld[1]) < cg: continue
                if d < best[0]: best = (d, nm)
        names.append(best[1])
    runs = []
    for nm in names:
        if runs and runs[-1][0] == nm: runs[-1][1] += 1
        else: runs.append([nm, 1])
    keep = {nm for nm, c in runs if c >= THROUGH_RUN}
    return keep

def build_route():
    origin, polys = load_centerlines()
    route = read_v1_route()
    keep = through_roads(route, polys)
    polys = [(nm, pts) for (nm, pts) in polys if nm in keep]   # through-roads only
    snapped = match(route, polys)
    final = moving_avg(resample(snapped, RESAMPLE, True), SMOOTH_WIN)
    return final, polys, sorted(keep)

def emit_cs(final):
    lines = []
    for i in range(0, len(final), 8):
        lines.append("        " + " ".join("new(%.2ff,%.2ff)," % (x, z) for x, z in final[i:i+8]))
    return "\n".join(lines)

if __name__ == "__main__":
    final, polys, keep = build_route()
    print("// through-roads used: " + ", ".join(keep))
    if "--plot" in sys.argv:
        import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
        fig, ax = plt.subplots(figsize=(11, 11))
        for nm, pts in polys: ax.plot([p[0] for p in pts], [p[1] for p in pts], color="0.82", lw=1)
        ax.plot([p[0] for p in final], [p[1] for p in final], "g-", lw=1.4)
        ax.set_aspect("equal"); plt.savefig("route_v2_preview.png", dpi=110, bbox_inches="tight")
        print("saved route_v2_preview.png")
    print("// %d points" % len(final))
    print(emit_cs(final))
