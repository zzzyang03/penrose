"""Build the navy play-mark icon as SVG / PNG / ICO with a transparent canvas."""

from __future__ import annotations

import math
import struct
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

NAVY = (12, 40, 87, 255)
WHITE = (247, 247, 247, 255)
LBLUE = (181, 205, 233, 255)
MBLUE = (70, 115, 182, 255)

SIZE = 1024
RADIUS = 228

# Play-mark: isosceles triangle pointing right, left side vertical.
A = (248.0, 232.0)
B = (248.0, 812.0)
C = (828.0, 534.0)
INSET = 108.0
EXTRUDE = (56.0, 42.0)
CORNER = 42.0


def sub(p: tuple[float, float], q: tuple[float, float]) -> tuple[float, float]:
    return (p[0] - q[0], p[1] - q[1])


def add(p: tuple[float, float], q: tuple[float, float]) -> tuple[float, float]:
    return (p[0] + q[0], p[1] + q[1])


def mul(p: tuple[float, float], s: float) -> tuple[float, float]:
    return (p[0] * s, p[1] * s)


def length(p: tuple[float, float]) -> float:
    return math.hypot(p[0], p[1])


def norm(p: tuple[float, float]) -> tuple[float, float]:
    n = length(p)
    return (p[0] / n, p[1] / n) if n else (0.0, 0.0)


def inset_triangle(
    a: tuple[float, float],
    b: tuple[float, float],
    c: tuple[float, float],
    dist: float,
) -> tuple[tuple[float, float], tuple[float, float], tuple[float, float]]:
    centroid = ((a[0] + b[0] + c[0]) / 3.0, (a[1] + b[1] + c[1]) / 3.0)

    def inward_normal(p: tuple[float, float], q: tuple[float, float]) -> tuple[float, float]:
        edge = norm(sub(q, p))
        n1 = (-edge[1], edge[0])
        mid = ((p[0] + q[0]) / 2.0, (p[1] + q[1]) / 2.0)
        if length(sub(centroid, add(mid, n1))) < length(sub(centroid, mid)):
            return n1
        return (edge[1], -edge[0])

    def offset_vertex(
        p: tuple[float, float], q: tuple[float, float], r: tuple[float, float]
    ) -> tuple[float, float]:
        nq = inward_normal(p, q)
        nr = inward_normal(p, r)
        v1 = sub(q, p)
        v2 = sub(r, p)
        o1 = add(p, mul(nq, dist))
        o2 = add(p, mul(nr, dist))
        det = v1[0] * (-v2[1]) - v1[1] * (-v2[0])
        if abs(det) < 1e-6:
            return add(p, mul(add(nq, nr), dist * 0.5))
        t = ((o2[0] - o1[0]) * (-v2[1]) - (o2[1] - o1[1]) * (-v2[0])) / det
        return add(o1, mul(v1, t))

    return offset_vertex(a, b, c), offset_vertex(b, a, c), offset_vertex(c, a, b)


def shift(p: tuple[float, float]) -> tuple[float, float]:
    return add(p, EXTRUDE)


def fmt(p: tuple[float, float]) -> str:
    return f"{p[0]:.1f},{p[1]:.1f}"


def path_polygon(pts: list[tuple[float, float]]) -> str:
    return "M " + " L ".join(fmt(p) for p in pts) + " Z"


def fillet_convex(pts: list[tuple[float, float]], radius: float, steps: int = 10) -> list[tuple[float, float]]:
    """Round the corners of a convex polygon (outer / inner silhouette)."""
    n = len(pts)
    out: list[tuple[float, float]] = []
    for i in range(n):
        prev = pts[(i - 1) % n]
        cur = pts[i]
        nxt = pts[(i + 1) % n]
        u = norm(sub(prev, cur))
        v = norm(sub(nxt, cur))
        r = min(radius, length(sub(prev, cur)) * 0.45, length(sub(nxt, cur)) * 0.45)
        start = add(cur, mul(u, r))
        end = add(cur, mul(v, r))
        a0 = math.atan2(start[1] - cur[1], start[0] - cur[0])
        a1 = math.atan2(end[1] - cur[1], end[0] - cur[0])
        da = a1 - a0
        while da <= -math.pi:
            da += 2 * math.pi
        while da > math.pi:
            da -= 2 * math.pi
        # Convex interior is the shorter inward sweep.
        out.append(start)
        for k in range(1, steps):
            ang = a0 + da * (k / steps)
            out.append((cur[0] + math.cos(ang) * r, cur[1] + math.sin(ang) * r))
        out.append(end)
    return out


def geometry() -> dict[str, list[tuple[float, float]]]:
    ai, bi, ci = inset_triangle(A, B, C, INSET)
    ae, be, ce = shift(A), shift(B), shift(C)
    aie, bie, cie = shift(ai), shift(bi), shift(ci)
    return {
        "side_bc": [B, C, ce, be],
        "side_ca": [C, A, ae, ce],
        "inner_right": [ci, ai, aie, cie],
        "inner_bottom": [bi, ci, cie, bie],
        "inner_left": [ai, bi, bie, aie],
        "light_front": [A, C, ci, ai],
        "white_front": [A, B, bi, ai],
        "white_bottom": [B, C, ci, bi],
        "outer": [A, B, C],
        "inner": [ai, bi, ci],
        "outer_back": [ae, be, ce],
    }


def svg_text() -> str:
    g = geometry()
    colors = {
        "side_bc": "#4673B6",
        "side_ca": "#4673B6",
        "inner_right": "#4673B6",
        "inner_bottom": "#4673B6",
        "inner_left": "#B5CDE9",
        "light_front": "#B5CDE9",
        "white_front": "#F7F7F7",
        "white_bottom": "#F7F7F7",
    }
    clip = path_polygon(fillet_convex(g["outer"], CORNER))
    hole = path_polygon(g["inner"])
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {SIZE} {SIZE}">',
        "  <!-- No canvas fill: only the navy plate is opaque. -->",
        f'  <rect width="{SIZE}" height="{SIZE}" rx="{RADIUS}" ry="{RADIUS}" fill="#0C2857"/>',
        f'  <clipPath id="plate"><rect width="{SIZE}" height="{SIZE}" rx="{RADIUS}" ry="{RADIUS}"/></clipPath>',
        f'  <clipPath id="mark"><path d="{clip} {hole}" clip-rule="evenodd"/></clipPath>',
        '  <g clip-path="url(#plate)">',
        '    <g clip-path="url(#mark)">',
    ]
    for key, color in colors.items():
        parts.append(f'      <path fill="{color}" d="{path_polygon(g[key])}"/>')
    parts += ["    </g>", "  </g>", "</svg>", ""]
    return "\n".join(parts)


def draw_icon(scale: int = 4) -> Image.Image:
    s = SIZE * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    plate = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(plate).rounded_rectangle((0, 0, s - 1, s - 1), radius=RADIUS * scale, fill=NAVY)

    mark = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(mark)
    geo = geometry()

    def poly(key: str, color: tuple[int, int, int, int]) -> None:
        d.polygon([(p[0] * scale, p[1] * scale) for p in geo[key]], fill=color)

    poly("side_bc", MBLUE)
    poly("side_ca", MBLUE)
    poly("inner_right", MBLUE)
    poly("inner_bottom", MBLUE)
    poly("inner_left", LBLUE)
    poly("light_front", LBLUE)
    poly("white_front", WHITE)
    poly("white_bottom", WHITE)

    # Soften the play-mark silhouette (outer rounded, hole rounded).
    sil = Image.new("L", (s, s), 0)
    sd = ImageDraw.Draw(sil)
    sd.polygon([(p[0] * scale, p[1] * scale) for p in fillet_convex(geo["outer"], CORNER)], fill=255)
    sd.polygon([(p[0] * scale, p[1] * scale) for p in geo["inner"]], fill=0)
    for key in ("side_bc", "side_ca"):
        sd.polygon([(p[0] * scale, p[1] * scale) for p in geo[key]], fill=255)
    red, green, blue, alpha = mark.split()
    mark = Image.merge("RGBA", (red, green, blue, ImageChops.multiply(alpha, sil)))

    out = Image.alpha_composite(plate, mark)
    clip = Image.new("L", (s, s), 0)
    ImageDraw.Draw(clip).rounded_rectangle((0, 0, s - 1, s - 1), radius=RADIUS * scale, fill=255)
    red, green, blue, alpha = out.split()
    out = Image.merge("RGBA", (red, green, blue, ImageChops.multiply(alpha, clip)))
    return out.resize((SIZE, SIZE), Image.Resampling.LANCZOS)


def write_png_ico(png: Image.Image, dest: Path, sizes: list[int]) -> None:
    """Windows ICO with PNG-compressed frames so alpha survives."""
    frames: list[bytes] = []
    for side in sizes:
        frame = png.resize((side, side), Image.Resampling.LANCZOS)
        buf = dest.with_suffix(f".{side}.tmp.png")
        frame.save(buf, "PNG")
        frames.append(buf.read_bytes())
        buf.unlink()

    count = len(sizes)
    offset = 6 + 16 * count
    chunks = [struct.pack("<HHH", 0, 1, count)]
    body = b""
    for side, data in zip(sizes, frames, strict=True):
        w = 0 if side >= 256 else side
        h = 0 if side >= 256 else side
        chunks.append(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset))
        body += data
        offset += len(data)
    dest.write_bytes(b"".join(chunks) + body)


def main() -> None:
    root = Path(__file__).resolve().parent
    assets = root.parent / "src" / "MediaPlayer.App.WinUI" / "Assets"
    svg_path = root / "icon_navy.svg"
    svg_path.write_text(svg_text(), encoding="utf-8")
    png = draw_icon()
    for x, y in ((0, 0), (SIZE - 1, 0), (0, SIZE - 1), (SIZE - 1, SIZE - 1)):
        if png.getpixel((x, y))[3] != 0:
            raise SystemExit(f"corner {x},{y} is not transparent: {png.getpixel((x, y))}")
    png.save(root / "penrose-icon-navy.png", "PNG")
    png.save(assets / "AppIcon.png", "PNG")
    write_png_ico(png, assets / "AppIcon.ico", [16, 20, 24, 32, 40, 48, 64, 128, 256])
    print(f"wrote {svg_path}")
    print(f"wrote {assets / 'AppIcon.png'} and {assets / 'AppIcon.ico'}")


if __name__ == "__main__":
    main()
