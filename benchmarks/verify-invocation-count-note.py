"""Verificateur independant de la note INVOCATION-COUNT-TIERING-META.md.

Il ne lit PAS la prose : il re-derive chaque chiffre cite depuis les artefacts
BDN (CSV) et les journaux BDN, puis le confronte a la valeur ecrite dans la note.
Producteur (la note) != verificateur (ce script).

Usage :
    python verify-invocation-count-note.py [chemin/vers/la/note.md] [repertoire/artefacts]

Le second argument est le repertoire qui contient les artefacts BDN (sous-repertoires
``art-*``) et les journaux (``bench-logs/``) : ce sont les resultats bruts du
protocole, volontairement hors depot (ils sont volumineux).
"""
import importlib.util
import re
import statistics
import sys
from pathlib import Path

import csv

ROOT = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else Path(__file__).resolve().parent.parent
LOGS = ROOT / "bench-logs"


def read_artifact(art):
    """Lit un rapport BDN : {methode: {mean_ms, ...}}. Sans dependance externe."""
    csvs = list((art / "results").glob("*-report.csv"))
    if not csvs:
        return {}
    unit = {"ns": 1e-6, "us": 1e-3, "ms": 1.0, "s": 1000.0}

    def to_ms(value):
        m = re.match(r"^([0-9.,]+)\s*(\S+)?$", value.strip())
        num = float(m.group(1).replace(",", ""))
        u = (m.group(2) or "ms").replace("µ", "u").replace("μ", "u")
        return num * unit[u]

    with csvs[0].open(encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.DictReader(fh, delimiter=";"))
    return {r["Method"]: {"mean_ms": to_ms(r["Mean"])} for r in rows}
M = {"RandomSearch_Ackley2D_10k": "Ackley2D_10k",
     "RandomSearch_Sphere10D_50k": "Sphere10D_50k",
     "CenterBias_Ackley_5k": "CenterBias_Ackley_5k",
     "CenterBias_Suite4_5k": "CenterBias_Suite4_5k"}


def col(pattern):
    out = {}
    for art in sorted(ROOT.glob(pattern)):
        p = int(art.name.rsplit("-p", 1)[1])
        d = read_artifact(art)
        if d:
            out[p] = {m: v["mean_ms"] for m, v in d.items()}
    return out


def med(c, m):
    v = [p[m] for p in c.values() if m in p]
    return statistics.median(v) if v else None


def ladder_point(cls, m):
    vals = []
    for art in sorted(ROOT.glob(f"art-ladder-{cls}-net9-p*")):
        d = read_artifact(art)
        if d and m in d:
            vals.append(d[m]["mean_ms"])
    return statistics.median(vals) if vals else None


def series_from_log(path, method):
    txt = Path(path).read_text(encoding="utf-8", errors="replace")
    for b in re.split(r"// Benchmark: ", txt)[1:]:
        if method not in b.split(":")[0]:
            continue
        res = re.findall(r"WorkloadResult\s+\d+: \d+ ops?, ([\d.]+) ns", b)
        if res:
            return [float(x) / 1e6 for x in res]
    return []


def main():
    note = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else (
        Path(__file__).resolve().parent / "INVOCATION-COUNT-TIERING-META.md")
    text = note.read_text(encoding="utf-8")
    # La note ecrit les decimales avec une virgule ; le gras markdown ne porte
    # aucune information pour le verificateur.
    flat = re.sub(r"(\d),(\d)", r"\1.\2", text).replace("*", "")

    # Sectionnement : chaque tableau est verifie dans SA section, sinon une
    # recherche par methode tombe sur la premiere table qui contient la ligne.
    def section(start, end=None):
        i = flat.find(start)
        if i < 0:
            return ""
        j = flat.find(end, i) if end else len(flat)
        return flat[i:j if j > 0 else len(flat)]

    sec1 = section("## Grille 1", "## Grille 2")
    sec2 = section("## Grille 2", "## Grille 3")
    sec3 = section("## Grille 3", "## Ce que cette note")

    A = {c: col(f"art-tiering{c}-OptimizerBenchmarks-net9-p*") for c in ("ON", "OFF")}
    checks = []

    def chk(label, quoted, derived, tol=0.02):
        ok = derived is not None and abs(quoted - derived) <= tol * max(abs(derived), 1e-9)
        checks.append((label, quoted, derived, ok))

    # --- Grille 1 : echelle N ---
    for m, short in M.items():
        row = re.search(rf"`{re.escape(m)}` \| ([\d.]+) \| ([\d.]+) \| ([\d.]+)", sec1)
        if not row:
            checks.append((f"grille1 {short} ligne introuvable", 0, None, False))
            continue
        vals = [float(x) for x in row.groups()]
        for cls, n, idx in (("Optimizer_Ladder_N1", 1, 0), ("Optimizer_Ladder_N8", 8, 1),
                            ("Optimizer_Ladder_N64", 64, 2)):
            chk(f"grille1 {short} N={n}", vals[idx], ladder_point(cls, m))

    # --- Grille 2 : default / off ---
    for m, short in M.items():
        row = re.search(rf"`{re.escape(m)}` \| ([\d.]+) \| ([\d.]+) \| ([\d.]+)", sec2)
        if not row:
            checks.append((f"grille2 {short} ligne introuvable", 0, None, False))
            continue
        d_def, d_off = med(A["ON"], m), med(A["OFF"], m)
        chk(f"grille2 {short} default", float(row.group(1)), d_def)
        chk(f"grille2 {short} off", float(row.group(2)), d_off)
        chk(f"grille2 {short} ratio", float(row.group(3)), d_off / d_def, tol=0.03)

    # --- Grille 3 : sept configurations ---
    G3 = {
        "default": "art-tieringON-OptimizerBenchmarks-net9-p*",
        "off": "art-tieringOFF-OptimizerBenchmarks-net9-p*",
        "seuil 10": "art-threshold10-OptimizerBenchmarks-net9-p*",
        "promote0": "art-promote0-OptimizerBenchmarks-net9-p*",
        "promote0_nopgo": "art-promote0_nopgo-OptimizerBenchmarks-net9-p*",
        "promote0nodelay": "art-promote0nodelay-OptimizerBenchmarks-net9-p*",
        "promote0nodelaynopgo": "art-promote0nodelaynopgo-OptimizerBenchmarks-net9-p*",
    }
    for label, pat in G3.items():
        c = col(pat)
        d = med(c, "RandomSearch_Ackley2D_10k")
        row = re.search(rf"\| `{re.escape(label)}` \|[^|]*\| ([\d.]+) \| ([\d.]+) \|", sec3)
        if row:
            chk(f"grille3 {label} valeur", float(row.group(1)), d)
            chk(f"grille3 {label} ratio", float(row.group(2)),
                d / med(A["ON"], "RandomSearch_Ackley2D_10k"), tol=0.03)
        else:
            checks.append((f"grille3 {label} ligne introuvable", 0, None, False))

    # --- Le profil par iteration : la marche, sur LES TROIS passes ---
    # La note affirme : marche aux iterations 8 / 7 / 8, moyennes de passe
    # 17,614 / 16,696 / 14,578, et phases avant/apres. On re-derive chaque
    # valeur depuis le journal BDN et on la confronte.
    passe_attendu = {1: (8, 27.57, 8.90, 17.614),
                     2: (7, 31.71, 6.69, 16.696),
                     3: (8, 24.32, 6.05, 14.578)}
    for p, (q_step, q_av, q_ap, q_mean) in passe_attendu.items():
        s = series_from_log(LOGS / f"ladder-Optimizer_Ladder_N1.net9.p{p}.log",
                            "RandomSearch_Sphere10D_50k")
        if len(s) < 12:
            checks.append((f"marche passe {p} serie introuvable", 0, None, False))
            continue
        best = max(range(1, len(s)), key=lambda i: s[i - 1] - s[i])
        av = sum(s[:best]) / best
        ap = sum(s[best:]) / (len(s) - best)
        mean = sum(s) / len(s)
        chk(f"marche p{p} : iteration", q_step, best + 1, tol=0)
        chk(f"marche p{p} : phase tier0", q_av, av, tol=0.02)
        chk(f"marche p{p} : phase tier1", q_ap, ap, tol=0.02)
        chk(f"marche p{p} : moyenne BDN", q_mean, mean, tol=0.005)

    # --- Les rapports cites : N=1 / cout convergent, et default/off ---
    for m, short in M.items():
        n1 = ladder_point("Optimizer_Ladder_N1", m)
        n64 = ladder_point("Optimizer_Ladder_N64", m)
        d_def, d_off = med(A["ON"], m), med(A["OFF"], m)
        # la note cite le rapport N=1/N=64 dans la section "Lecture"
        sec_lecture = section("Lecture :", "### L'arithm")
        r = re.search(rf"`{re.escape(m)}` (×\d+(?:[.,]\d+)?)", sec_lecture)
        if r:
            chk(f"rapport N1/N64 {short}", float(r.group(1).replace(",", ".").lstrip("×")),
                n1 / n64, tol=0.02)
        else:
            checks.append((f"rapport N1/N64 {short} non cite", 0, None, False))

    # --- Les 9 paires appariees Ackley2D ---
    pairs = []
    for rt in ("net9", "net10", "net11"):
        on, off = [], []
        for p in (1, 2, 3):
            a = read_artifact(ROOT / f"art-tieringON-OptimizerBenchmarks-{rt}-p{p}")
            b = read_artifact(ROOT / f"art-tieringOFF-OptimizerBenchmarks-{rt}-p{p}")
            if a and b:
                on.append(a["RandomSearch_Ackley2D_10k"]["mean_ms"])
                off.append(b["RandomSearch_Ackley2D_10k"]["mean_ms"])
        pairs += [round(o / n, 2) for o, n in zip(off, on)]
    # le texte "plat" a remplace les virgules decimales par des points
    rows = re.findall(r"\| 0\.(\d\d) \| 0\.(\d\d) \| 0\.(\d\d)", flat)
    quoted = [float(x) for row in rows for x in row]
    if len(quoted) == 9:
        for i, rt in enumerate(("net9", "net10", "net11")):
            on, off = [], []
            for p in (1, 2, 3):
                a = read_artifact(ROOT / f"art-tieringON-OptimizerBenchmarks-{rt}-p{p}")
                b = read_artifact(ROOT / f"art-tieringOFF-OptimizerBenchmarks-{rt}-p{p}")
                on.append(a["RandomSearch_Ackley2D_10k"]["mean_ms"])
                off.append(b["RandomSearch_Ackley2D_10k"]["mean_ms"])
            for j in range(3):
                chk(f"paire {rt} p{j+1}", quoted[i * 3 + j] / 100, off[j] / on[j], tol=0.02)
    else:
        checks.append((f"tableau des paires : {len(quoted)}/9 valeurs trouvees",
                       0, None, False))

    # --- Rapport final ---
    ko = [c for c in checks if not c[3]]
    for label, q, d, ok in checks:
        ds = "absent" if d is None else f"{d:.4f}"
        print(f"[{'OK ' if ok else 'ECHEC'}] {label:38s} note={q:<10} derive={ds}")
    print(f"\n{len(checks) - len(ko)}/{len(checks)} verifications concordent.")
    if ko:
        print("ECARTS :")
        for label, q, d, _ in ko:
            print(f"  - {label} : note={q} derive={d}")
        sys.exit(1)


if __name__ == "__main__":
    main()
