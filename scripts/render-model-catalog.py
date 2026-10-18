"""Render a review sheet from actual exported RimFront CPU model buffers.

Requires Pillow and NumPy. This is an offline geometry inspection, not a game screenshot.
Pass --row-scale to compare each row at a common scale and --cover-resources
to select the cover and quantity examples from the object catalog export.
"""
import json
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import numpy as np


def main():
    models = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    if "--cover-resources" in sys.argv[4:]:
        names = ["Barricade / wood", "Barricade / steel", "Embrasure / stone", "Embrasure / steel"]
        names += [f"{kind} / {count} {'unit' if count == 1 else 'units'}"
                  for kind in ["Wood", "Steel", "Stone blocks"] for count in [1, 25, 50, 75]]
        lookup = {model["Name"]: model for model in models}
        models = [lookup[name] for name in names]
    output = Path(sys.argv[2])
    scale, columns, width, height = 2, 4, 330, 370
    row_scale = "--row-scale" in sys.argv[4:]
    projections = [[(.83 * x - .55 * z, .30 * x + .45 * z - y, .55 * x + .83 * z + .30 * y)
                    for x, y, z in model["Vertices"]] for model in models]
    rows = math.ceil(len(models) / columns)
    image = Image.new("RGB", (columns * width * scale, (100 + rows * height) * scale), "#101924")
    draw = ImageDraw.Draw(image)
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    font = ImageFont.truetype(str(font_path), 18 * scale) if font_path.exists() else ImageFont.load_default()
    small = ImageFont.truetype(str(font_path), 12 * scale) if font_path.exists() else ImageFont.load_default()
    title = ImageFont.truetype(str(font_path), 28 * scale) if font_path.exists() else font
    draw.text((24 * scale, 14 * scale), sys.argv[3] if len(sys.argv) > 3 else "RimFront 0.5 / pawn geometry catalog", fill="#e7f0f9", font=title)
    draw.text((24 * scale, 54 * scale), "Actual compiled CPU meshes and pose helpers • offline projection, not in-game rendering", fill="#9cb0c5", font=small)
    scale_note = "Each row shares one scale and ground origin; quantities can be compared. Runtime lighting is not shown." if row_scale else "Each cell fits its mesh independently; runtime scale, motion and lighting are not shown."
    draw.text((24 * scale, 74 * scale), scale_note, fill="#9cb0c5", font=small)
    for index, model in enumerate(models):
        ox, oy = (index % columns) * width, 100 + (index // columns) * height
        draw.rounded_rectangle((ox * scale + 8, oy * scale + 4, (ox + width) * scale - 8, (oy + height) * scale - 12), radius=14, fill="#1b2939", outline="#33485d", width=2)
        draw.text(((ox + 16) * scale, (oy + 12) * scale), model["Name"], font=font, fill="#e7f0f9")
        vertices, colors, indices = model["Vertices"], model["Colors"], model["Triangles"]
        projected = projections[index]
        extent = [point for projection in projections[index // columns * columns:(index // columns + 1) * columns] for point in projection] if row_scale else projected
        lo_x, hi_x = min(p[0] for p in extent), max(p[0] for p in extent)
        lo_y, hi_y = min(p[1] for p in extent), max(p[1] for p in extent)
        factor = min(270 / max(.01, hi_x - lo_x), 265 / max(.01, hi_y - lo_y))
        center_x, center_y = (lo_x + hi_x) / 2, (lo_y + hi_y) / 2
        # Orthographic CPU depth buffer: triangle sorting alone misrenders intersecting apparel.
        pixels = np.empty((280 * scale, 306 * scale, 3), dtype=np.uint8)
        pixels[:] = (27, 41, 57)
        depth = np.full(pixels.shape[:2], -np.inf)
        for offset in range(0, len(indices), 3):
            ids = indices[offset:offset + 3]
            polygon = [((153 + (projected[i][0] - center_x) * factor) * scale, (140 + (projected[i][1] - center_y) * factor) * scale) for i in ids]
            (ax, ay), (bx, by), (cx, cy) = polygon
            denominator = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(denominator) < 1e-8:
                continue
            x0, x1 = max(0, math.floor(min(p[0] for p in polygon))), min(pixels.shape[1], math.ceil(max(p[0] for p in polygon)))
            y0, y1 = max(0, math.floor(min(p[1] for p in polygon))), min(pixels.shape[0], math.ceil(max(p[1] for p in polygon)))
            if x0 >= x1 or y0 >= y1:
                continue
            yy, xx = np.mgrid[y0:y1, x0:x1]
            xx, yy = xx + .5, yy + .5
            wa = ((by - cy) * (xx - cx) + (cx - bx) * (yy - cy)) / denominator
            wb = ((cy - ay) * (xx - cx) + (ax - cx) * (yy - cy)) / denominator
            wc = 1 - wa - wb
            zz = wa * projected[ids[0]][2] + wb * projected[ids[1]][2] + wc * projected[ids[2]][2]
            mask = (wa >= -1e-9) & (wb >= -1e-9) & (wc >= -1e-9) & (zz > depth[y0:y1, x0:x1])
            depth[y0:y1, x0:x1][mask] = zz[mask]
            rgb = np.clip(np.array([sum(colors[i][c] for i in ids) / 3 * 255 for c in range(3)]), 0, 255).astype(np.uint8)
            pixels[y0:y1, x0:x1][mask] = rgb
        image.paste(Image.fromarray(pixels), ((ox + 12) * scale, (oy + 43) * scale))
        draw.text(((ox + 16) * scale, (oy + 334) * scale), f"{len(vertices):,} vertices / {len(indices) // 3:,} triangles", font=small, fill="#9cb0c5")
    image.resize((image.width // scale, image.height // scale), Image.Resampling.LANCZOS).save(output)
    print(f"Catalog: {output} ({len(models)} actual mesh samples)")


if __name__ == "__main__":
    main()
