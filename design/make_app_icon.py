"""Turn the original navy icon (white canvas) into a transparent-corner PNG + ICO."""

from __future__ import annotations

import struct
import sys
from collections import deque
from pathlib import Path

from PIL import Image

HARD = 248
FRINGE = 3


def knock_out(src: Path) -> Image.Image:
    img = Image.open(src).convert("RGBA")
    w, h = img.size
    pix = img.load()
    assert pix is not None
    bg = [[False] * w for _ in range(h)]
    q: deque[tuple[int, int]] = deque()
    for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        r, g, b, _ = pix[x, y]
        if min(r, g, b) >= HARD and max(r, g, b) - min(r, g, b) <= 12:
            bg[y][x] = True
            q.append((x, y))
    while q:
        x, y = q.popleft()
        for nx, ny in (
            (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1),
            (x - 1, y - 1), (x + 1, y - 1), (x - 1, y + 1), (x + 1, y + 1),
        ):
            if nx < 0 or ny < 0 or nx >= w or ny >= h or bg[ny][nx]:
                continue
            r, g, b, _ = pix[nx, ny]
            if min(r, g, b) >= HARD and max(r, g, b) - min(r, g, b) <= 12:
                bg[ny][nx] = True
                q.append((nx, ny))
    fringe = [[False] * w for _ in range(h)]
    ring = [(x, y) for y in range(h) for x in range(w) if bg[y][x]]
    for _ in range(FRINGE):
        nxt: list[tuple[int, int]] = []
        for x, y in ring:
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if nx < 0 or ny < 0 or nx >= w or ny >= h or bg[ny][nx] or fringe[ny][nx]:
                    continue
                fringe[ny][nx] = True
                nxt.append((nx, ny))
        ring = nxt
    for y in range(h):
        for x in range(w):
            r, g, b, a = pix[x, y]
            if bg[y][x]:
                pix[x, y] = (r, g, b, 0)
                continue
            if not fringe[y][x]:
                continue
            alpha_f = 1.0 - min(r, g, b) / 255.0
            if alpha_f <= 0:
                pix[x, y] = (r, g, b, 0)
                continue
            pix[x, y] = (
                max(0, min(255, round((r - 255 * (1 - alpha_f)) / alpha_f))),
                max(0, min(255, round((g - 255 * (1 - alpha_f)) / alpha_f))),
                max(0, min(255, round((b - 255 * (1 - alpha_f)) / alpha_f))),
                max(0, min(255, round(alpha_f * 255))),
            )
    return img


def write_png_ico(png: Image.Image, dest: Path) -> None:
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames: list[bytes] = []
    for side in sizes:
        buf = dest.with_suffix(f".{side}.tmp.png")
        png.resize((side, side), Image.Resampling.LANCZOS).save(buf, "PNG")
        frames.append(buf.read_bytes())
        buf.unlink()
    offset = 6 + 16 * len(sizes)
    chunks = [struct.pack("<HHH", 0, 1, len(sizes))]
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
    assets = root.parent / "src" / "Penrose.App.WinUI" / "Assets"
    if len(sys.argv) != 2:
        raise SystemExit("usage: make_app_icon.py <original-white-canvas.png>")
    original = Path(sys.argv[1])
    if not original.is_file():
        raise SystemExit(f"original icon not found: {original}")
    png = knock_out(original)
    if png.getpixel((0, 0))[3] != 0:
        raise SystemExit("corner is still opaque")
    png.save(assets / "AppIcon.png", "PNG")
    write_png_ico(png, assets / "AppIcon.ico")
    print(f"wrote {assets / 'AppIcon.png'} and {assets / 'AppIcon.ico'}")


if __name__ == "__main__":
    main()
