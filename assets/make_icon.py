# -*- coding: utf-8 -*-
"""desktop-calendar 品牌图标生成脚本（可复跑，不联网）。

用法：
    python make_icon.py                 # 生成四个候选 + 以 cand-a 为默认写 app.ico
    python make_icon.py --pick cand-d   # 换成 cand-d 重写 app.ico / logo-256 / app-32 / app-16

产物（全部落在本脚本所在目录）：
    cand-{a,b,c,d}-256.png cand-{a,b,c,d}-32.png   各候选的 256 / 32 单帧
    app.ico                             多尺寸 ICO（16/24/32/48/64/128/256，逐帧内嵌 PNG）
    logo-256.png                        256 单帧，给 README
    app-32.png app-16.png               1:1 单帧，证明小尺寸可读
    icon-preview.png                    人眼一次看清所有尺寸的预览图
    small-compare.png                   cand-a 与 cand-d 的 16/24/32/48 上下对照（x4 最近邻）

画法：全部在 2048px（= 256*8）超采样画布上用 alpha 遮罩合成，再按每个目标尺寸
用 LANCZOS 单独缩放，圆角一律走遮罩 alpha，不做硬裁。

尺寸分档：cand-d 走「小尺寸简化」——16/24/32 只留蓝底 + 白页 + 数字，装订孔与页顶
条带在 >=48 才画（与 iOS / 微软图标同款策略），所以它是按尺寸渲染而非单纯缩放。
"""

import argparse
import io
import os
import struct
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

SUPERSAMPLE = 8
MASTER = 256 * SUPERSAMPLE          # 2048
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
SMALL_MAX = 32                      # <= 此尺寸走「小尺寸简化」细节档
CANDIDATES = ["cand-a", "cand-b", "cand-c", "cand-d"]
DEFAULT_PICK = "cand-a"
COMPARE = ("cand-a", "cand-d")

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- 品牌色（与 Branding.cs 一致）
BLUE_TOP = (0x5B, 0x8D, 0xFF)
BLUE_BOTTOM = (0x2F, 0x5B, 0xE0)
BLUE_TOP_D = (0x4A, 0x79, 0xF0)     # cand-d：再深半档，提高与白页的明度差
BLUE_BOTTOM_D = (0x24, 0x50, 0xC8)
INK = (0x1E, 0x2A, 0x44)            # 深蓝墨色数字（cand-a）
INK_D = (0x14, 0x1F, 0x36)          # cand-d 数字：再压暗一点，小尺寸更抓眼
BLACK = (0x11, 0x18, 0x27)          # cand-c 数字
RED = (0xEF, 0x44, 0x44)            # cand-c 顶部条带
PAGE_WHITE = (0xFF, 0xFF, 0xFF)
PAGE_BAND = (0xE4, 0xEC, 0xFF)      # cand-a / cand-d 页面顶部浅色条带
BAND_SEP = (0xC9, 0xD8, 0xFF)       # cand-a 条带分隔线
BAND_SEP_D = (0xB9, 0xC8, 0xF2)     # cand-d 条带分隔线
DOT = (0x2C, 0x50, 0xB8)            # cand-b 圆点

# cand-d 的数字比几何居中再往上收一点：下边距 = 上边距 * 0.62
BOTTOM_MARGIN_RATIO = 0.62

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\seguisb.ttf",
    r"C:\Windows\Fonts\msyhbd.ttc",
]
# Segoe UI Black 比 Bold 重一档，小尺寸笔画不会散开
FONT_BLACK_CANDIDATES = [
    r"C:\Windows\Fonts\seguibl.ttf",
    r"C:\Windows\Fonts\segoeuib.ttf",
]


def _find_font(paths, what):
    for p in paths:
        if os.path.isfile(p):
            return p
    raise SystemExit("找不到系统字体：" + what)


FONT_PATH = _find_font(FONT_CANDIDATES, "segoeuib.ttf / msyhbd.ttc")
FONT_BLACK = _find_font(FONT_BLACK_CANDIDATES, "seguibl.ttf / segoeuib.ttf")


# ---------------------------------------------------------------- 基础绘制工具
def canvas():
    return Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))


def round_mask(box, radius):
    """圆角矩形 alpha 遮罩（2048 画布上绘制，靠 8x 降采样得到抗锯齿）。"""
    m = Image.new("L", (MASTER, MASTER), 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=int(round(radius)), fill=255)
    return m


def top_round_mask(box, radius, bottom):
    """只有上面两角是圆的遮罩：在 bottom 处截断一个足够高的圆角矩形。"""
    big = (box[0], box[1], box[2], box[1] + (bottom - box[1]) + radius * 2 + 8)
    m = Image.new("L", (MASTER, MASTER), 0)
    ImageDraw.Draw(m).rounded_rectangle(big, radius=int(round(radius)), fill=255)
    cut = Image.new("L", (MASTER, MASTER), 0)
    cut.paste(m.crop((0, 0, MASTER, bottom)), (0, 0))
    return cut


def composite(base, fill, mask):
    """按遮罩 alpha 合成。fill 可以是颜色元组或整幅 RGBA 图。"""
    layer = fill.copy() if isinstance(fill, Image.Image) else Image.new("RGBA", (MASTER, MASTER), fill)
    layer.putalpha(mask)
    base.alpha_composite(layer)


def gradient(c0, c1):
    """左上→右下的线性渐变（c0 在左上）。"""
    import numpy as np
    y, x = np.mgrid[0:MASTER, 0:MASTER].astype("float32")
    t = (x * 0.35 + y * 0.65) / float(MASTER - 1)
    t = np.clip(t, 0.0, 1.0)[..., None]
    a = np.array(c0, dtype="float32")
    b = np.array(c1, dtype="float32")
    rgb = a * (1.0 - t) + b * t
    alpha = np.full((MASTER, MASTER, 1), 255.0, dtype="float32")
    return Image.fromarray(np.concatenate([rgb, alpha], axis=2).astype("uint8"), "RGBA")


def text_tile(text, px_height, color, font_path=None, stroke_ratio=0.0):
    """把一段文字渲染成紧贴内容（tight bbox）的图，再精确缩放到 px_height 高。

    stroke_ratio 给字形做等高比例的描边膨胀（换不到更重的字重时用它加粗）。
    """
    f = ImageFont.truetype(font_path or FONT_PATH, 420)
    stroke = int(round(420 * stroke_ratio))
    tmp = Image.new("RGBA", (1800, 800), (0, 0, 0, 0))
    ImageDraw.Draw(tmp).text((140, 400), text, font=f, fill=color, anchor="lm",
                             stroke_width=stroke, stroke_fill=color)
    bbox = tmp.getbbox()
    if bbox is None:
        raise SystemExit("文字渲染失败：" + text)
    crop = tmp.crop(bbox)
    w = max(1, int(round(crop.width * float(px_height) / crop.height)))
    return crop.resize((w, int(round(px_height))), Image.LANCZOS)


def paste_center(base, tile, cx, cy):
    base.alpha_composite(tile, (int(round(cx - tile.width / 2.0)), int(round(cy - tile.height / 2.0))))


def rounded_tile(rgba, size, bg=None):
    """取整幅画布的降采样结果；bg 非空时把透明区铺在 bg 上（预览图用）。"""
    img = rgba.resize((size, size), Image.LANCZOS)
    if bg is None:
        return img
    out = Image.new("RGBA", (size, size), bg)
    out.alpha_composite(img)
    return out


# ---------------------------------------------------------------- 候选
def draw_cand_a(detail="full"):
    """蓝色渐变圆角方块 + 白色日历页 + 页顶浅色条带 + 两条装订孔 + 大号「31」。"""
    img = canvas()
    outer = (0, 0, MASTER - 1, MASTER - 1)
    radius = MASTER * 0.22
    composite(img, gradient(BLUE_TOP, BLUE_BOTTOM), round_mask(outer, radius))

    px, py = MASTER * 0.135, MASTER * 0.185
    pw, ph = MASTER * 0.730, MASTER * 0.700
    page = (px, py, px + pw, py + ph)
    page_r = MASTER * 0.055

    # 两条装订孔（在页面之前画，下半截被页面盖住 => 读作圆环）
    hw, hh = MASTER * 0.075, MASTER * 0.170
    top = MASTER * 0.105
    for cx in (MASTER * 0.335, MASTER * 0.665):
        composite(img, PAGE_WHITE,
                  round_mask((cx - hw / 2.0, top, cx + hw / 2.0, top + hh), hw / 2.0))

    composite(img, PAGE_WHITE, round_mask(page, page_r))

    # 页顶浅色条带 + 1px 分隔线（条带圆角只在上两角）
    band_h = ph * 0.175
    band_bottom = int(round(py + band_h))
    composite(img, PAGE_BAND, top_round_mask((px, py, px + pw, band_bottom), page_r, band_bottom))
    sep = int(round(MASTER * 0.006))
    composite(img, BAND_SEP,
              round_mask((px, band_bottom, px + pw, band_bottom + sep), 0))

    num = text_tile("31", ph * 0.400, INK)
    paste_center(img, num, px + pw / 2.0, band_bottom + (py + ph - band_bottom) / 2.0)
    return img


def draw_cand_b(detail="full"):
    """同色底 + 白色日历页 + 页内 3x3 圆点阵（不依赖字体）。"""
    img = canvas()
    radius = MASTER * 0.22
    composite(img, gradient(BLUE_TOP, BLUE_BOTTOM), round_mask((0, 0, MASTER - 1, MASTER - 1), radius))

    px, py = MASTER * 0.135, MASTER * 0.185
    pw, ph = MASTER * 0.730, MASTER * 0.700
    page = (px, py, px + pw, py + ph)
    page_r = MASTER * 0.055

    hw, hh = MASTER * 0.075, MASTER * 0.170
    top = MASTER * 0.105
    for cx in (MASTER * 0.335, MASTER * 0.665):
        composite(img, PAGE_WHITE,
                  round_mask((cx - hw / 2.0, top, cx + hw / 2.0, top + hh), hw / 2.0))

    composite(img, PAGE_WHITE, round_mask(page, page_r))

    # 3x3 圆点阵：点半径 / 间距按画布比例给，缩小后仍是清晰的方格
    dot_r = MASTER * 0.052
    step = MASTER * 0.180
    cxc = px + pw / 2.0
    cyc = py + ph / 2.0
    for row in (-1, 0, 1):
        for col in (-1, 0, 1):
            cx = cxc + col * step
            cy = cyc + row * step
            composite(img, DOT, round_mask((cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r), dot_r))
    return img


def draw_cand_c(detail="full"):
    """最接近苹果：白底圆角方块 + 顶部红条带 + 黑色大号「31」+ 极淡内阴影。"""
    img = canvas()
    ra = MASTER * 0.22
    outer = (0, 0, MASTER - 1, MASTER - 1)
    tile = round_mask(outer, ra)
    composite(img, PAGE_WHITE, tile)

    band_bottom = int(round(MASTER * 0.255))
    composite(img, RED, top_round_mask((0, 0, MASTER - 1, band_bottom), ra, band_bottom))

    num = text_tile("31", MASTER * 0.340, BLACK)
    paste_center(img, num, MASTER / 2.0, band_bottom + (MASTER - band_bottom) / 2.0)

    # 极淡内阴影：整块遮罩减去内缩遮罩得到边缘环，高斯模糊后低 alpha 压在边上
    inset = int(round(MASTER * 0.012))
    inner = round_mask((outer[0] + inset, outer[1] + inset, outer[2] - inset, outer[3] - inset), ra - inset)
    ring = ImageChops.subtract(tile, inner).filter(ImageFilter.GaussianBlur(MASTER * 0.012))
    ring = ring.point(lambda v: int(v * 0.16))
    shade = Image.new("RGBA", (MASTER, MASTER), (0x0B, 0x12, 0x20, 255))
    shade.putalpha(ring)
    img.alpha_composite(shade)
    return img


def draw_cand_d(detail="full"):
    """cand-a 的蓝色身份 + 小尺寸可读性：白页放大 9%，细节只在 >=48 画，数字用 Black 字重。

    detail == "small"（16/24/32）时省略装订孔与页顶条带，只留蓝底 + 白页 + 数字。
    """
    full = (detail == "full")
    img = canvas()
    outer = (0, 0, MASTER - 1, MASTER - 1)
    radius = MASTER * 0.22
    composite(img, gradient(BLUE_TOP_D, BLUE_BOTTOM_D), round_mask(outer, radius))

    # 白页按页面中心等比放大 ~9%（cand-a 是 0.730 x 0.700）
    pw, ph = MASTER * 0.796, MASTER * 0.763
    px, py = (MASTER - pw) / 2.0, (MASTER - ph) / 2.0
    page = (px, py, px + pw, py + ph)
    page_r = MASTER * 0.055

    if full:
        hw, hh = MASTER * 0.070, MASTER * 0.150
        top = MASTER * 0.032
        for cx in (MASTER * 0.335, MASTER * 0.665):
            composite(img, PAGE_WHITE,
                      round_mask((cx - hw / 2.0, top, cx + hw / 2.0, top + hh), hw / 2.0))

    composite(img, PAGE_WHITE, round_mask(page, page_r))

    y0 = py
    if full:
        band_h = ph * 0.175
        band_bottom = int(round(py + band_h))
        composite(img, PAGE_BAND, top_round_mask((px, py, px + pw, band_bottom), page_r, band_bottom))
        sep = int(round(MASTER * 0.006))
        composite(img, BAND_SEP_D, round_mask((px, band_bottom, px + pw, band_bottom + sep), 0))
        y0 = band_bottom

    # 数字：小尺寸给更大字面（0.520 x 页高），大尺寸 0.430；Segoe UI Black 加粗一档
    num_h = ph * (0.430 if full else 0.520)
    num = text_tile("31", num_h, INK_D, font_path=FONT_BLACK)

    # 视觉居中：下边距 = 上边距 * BOTTOM_MARGIN_RATIO，而不是几何居中
    span = (py + ph) - y0
    top_gap = (span - num_h) / (1.0 + BOTTOM_MARGIN_RATIO)
    paste_center(img, num, px + pw / 2.0, y0 + top_gap + num_h / 2.0)
    return img


BUILDERS = {"cand-a": draw_cand_a, "cand-b": draw_cand_b,
            "cand-c": draw_cand_c, "cand-d": draw_cand_d}

_MASTER_CACHE = {}


def detail_for(size):
    return "small" if size <= SMALL_MAX else "full"


def get_master(name, detail):
    key = (name, detail)
    if key not in _MASTER_CACHE:
        _MASTER_CACHE[key] = BUILDERS[name](detail)
    return _MASTER_CACHE[key]


def build_frame(name, size):
    """按目标尺寸取正确细节档的超采样母图，再 LANCZOS 缩到 size。"""
    return get_master(name, detail_for(size)).resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------- ICO 写出（逐帧 PNG）
def write_ico(path, name, sizes):
    frames = []
    for s in sizes:
        buf = io.BytesIO()
        build_frame(name, s).save(buf, format="PNG", optimize=True)
        frames.append((s, buf.getvalue()))
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries = b""
    blob = b""
    for s, data in frames:
        dim = 0 if s >= 256 else s
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blob += data
        offset += len(data)
    with open(path, "wb") as f:
        f.write(header + entries + blob)


# ---------------------------------------------------------------- 小尺寸量化
TASKBAR = (32, 32, 32)


def luminance(rgb):
    r, g, b = rgb
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def frame_stats(name, size, bg=TASKBAR):
    """把该尺寸的真实帧合成到深色任务栏上，返回 (mean, std, contrast, levels)。"""
    fr = build_frame(name, size).convert("RGBA")
    base = Image.new("RGBA", (size, size), bg)
    base.alpha_composite(fr)
    comp = base.convert("RGB")
    px = comp.load()
    lums = []
    for y in range(size):
        for x in range(size):
            lums.append(luminance(px[x, y]))
    n = float(len(lums))
    mean = sum(lums) / n
    std = (sum((v - mean) ** 2 for v in lums) / n) ** 0.5
    lo, hi = min(lums), max(lums)
    contrast = (hi + 5.0) / (lo + 5.0)
    buckets = [0] * 8
    for v in lums:
        buckets[min(7, int(v / 32))] += 1
    levels = sum(1 for b in buckets if b / n >= 0.03)
    return mean, std, contrast, levels


# ---------------------------------------------------------------- 预览图
LABEL_FONT_CACHE = {}


def label_font(size):
    if size not in LABEL_FONT_CACHE:
        try:
            LABEL_FONT_CACHE[size] = ImageFont.truetype(FONT_PATH, size)
        except Exception:
            LABEL_FONT_CACHE[size] = ImageFont.load_default()
    return LABEL_FONT_CACHE[size]


def make_preview(path, pick):
    bg = (242, 244, 248)
    panel = (231, 236, 245)
    panel_dark = (24, 27, 34)
    ink = (30, 34, 42)
    muted = (110, 118, 132)

    pad = 26
    gutter = 132          # 左侧「cand-x」标签列
    cell_w = 272          # 每个尺寸占一列，256 也放得下
    native_h = 296
    head_h = 60
    gap = 20
    zoom_h = 168
    dark_h = 156
    rows = len(CANDIDATES)
    width = pad * 2 + gutter + cell_w * 7
    height = pad + (head_h + native_h + gap) * rows + zoom_h + dark_h + pad

    out = Image.new("RGBA", (width, height), bg)
    d = ImageDraw.Draw(out)
    f15 = label_font(15)
    f13 = label_font(13)
    f22 = label_font(22)
    f16 = label_font(16)

    y = pad
    for name in CANDIDATES:
        d.text((pad, y + 4), name + ("   (default)" if name == pick else ""),
               font=f22, fill=ink)
        sub = "native sizes, 1:1 on light background"
        if name == "cand-d":
            sub += "   (16/24/32 omit binding holes + top band)"
        d.text((pad, y + 32), sub, font=f13, fill=muted)
        y += head_h

        d.rounded_rectangle((pad - 10, y - 6, width - pad + 10, y + native_h - 12), 10, fill=panel)
        x = pad + gutter
        for size in (256, 128, 64, 48, 32, 24, 16):
            tile = Image.new("RGBA", (size, size), bg)
            tile.alpha_composite(build_frame(name, size))
            tx = x + (cell_w - 22 - size) // 2
            out.alpha_composite(tile, (tx, y))
            d.rectangle((tx - 1, y - 1, tx + size, y + size), outline=(203, 210, 224))
            d.text((x + 6, y + 266), "%dpx" % size, font=f15, fill=muted)
            x += cell_w
        y += native_h + gap

    # 底下一行：16 / 32 最近邻放大到 96px（像素颗粒一眼可见）
    d.text((pad, y + 2), "bottom row: nearest-neighbour zoom to 96px (16px and 32px)", font=f16, fill=ink)
    y += 28
    d.rounded_rectangle((pad - 10, y - 6, width - pad + 10, y + zoom_h - 30), 10, fill=panel)
    gx = pad + gutter
    for name in CANDIDATES:
        d.text((gx, y - 2), name, font=f15, fill=ink)
        zx = gx
        for size in (16, 32):
            tile = Image.new("RGBA", (size, size), bg)
            tile.alpha_composite(build_frame(name, size))
            tile = tile.resize((96, 96), Image.NEAREST)
            out.alpha_composite(tile, (zx, y + 24))
            d.rectangle((zx - 1, y + 23, zx + 96, y + 120), outline=(203, 210, 224))
            d.text((zx, y + 126), "%dpx x%d" % (size, 96 // size), font=f13, fill=muted)
            zx += 116
        gx += 300
    y += zoom_h

    # 暗色托盘条：16px 在深色任务栏上的实际观感
    d.rounded_rectangle((pad - 10, y, width - pad + 10, y + dark_h - 24), 10, fill=panel_dark)
    d.text((pad, y + 6), "tray 16px on a dark taskbar (1:1 and x3) + 16px contrast", font=f15, fill=(190, 196, 208))
    x = pad + gutter
    for name in CANDIDATES:
        t16 = Image.new("RGBA", (16, 16), panel_dark)
        t16.alpha_composite(build_frame(name, 16))
        out.alpha_composite(t16, (x, y + 34))
        out.alpha_composite(t16.resize((48, 48), Image.NEAREST), (x + 30, y + 22))
        mean, std, contrast, levels = frame_stats(name, 16)
        d.text((x, y + 78), name, font=f13, fill=(150, 158, 172))
        d.text((x, y + 94), "contrast %.1f  lv %d" % (contrast, levels), font=f13, fill=(150, 158, 172))
        x += 260

    out.convert("RGB").save(path, format="PNG", optimize=True)


def make_small_compare(path):
    """cand-a 与 cand-d 在 16/24/32/48 的上下对照：浅底 + 深底各一列，统一 x4 最近邻。"""
    light = (242, 244, 248)
    dark = TASKBAR
    ink = (30, 34, 42)
    muted = (110, 118, 132)
    panel = (231, 236, 245)
    panel_dark = (24, 27, 34)
    white = (236, 240, 246)

    pad = 24
    gutter = 118
    tile = 192           # 48px * 4
    colgap = 16
    rowgap = 26
    sizes = (16, 24, 32, 48)
    cols = []
    for bg, bg_name in ((light, "light"), (dark, "dark")):
        for name in COMPARE:
            cols.append((name, bg, bg_name))

    width = pad * 2 + gutter + len(cols) * tile + (len(cols) - 1) * colgap
    head = 92
    grid_h = len(sizes) * (tile + rowgap)
    foot = 190
    height = pad + head + grid_h + foot + pad

    out = Image.new("RGBA", (width, height), (255, 255, 255))
    d = ImageDraw.Draw(out)
    f22 = label_font(22)
    f15 = label_font(15)
    f13 = label_font(13)

    d.text((pad, pad - 2), "cand-a  vs  cand-d   -   small sizes, nearest-neighbour x4", font=f22, fill=ink)
    d.text((pad, pad + 30), "left pair on light background, right pair on dark taskbar #202020", font=f13, fill=muted)
    d.text((pad, pad + 50), "cand-d: white page +9%, binding holes and top band omitted at 16/24/32, Segoe UI Black digits",
           font=f13, fill=muted)

    y = pad + head
    x = pad + gutter
    for name, bg, bg_name in cols:
        d.text((x, y - 22), "%s / %s" % (name[-1], bg_name), font=f15, fill=ink)
        x += tile + colgap

    for size in sizes:
        lab = "%dpx" % size
        d.text((pad, y + tile // 2 - 16), lab, font=f15, fill=ink)
        d.text((pad, y + tile // 2 + 2), "x%d" % (tile // size), font=f13, fill=muted)
        x = pad + gutter
        for name, bg, bg_name in cols:
            inner = Image.new("RGBA", (size, size), bg)
            inner.alpha_composite(build_frame(name, size))
            big = inner.resize((tile, tile), Image.NEAREST)
            out.alpha_composite(big, (x, y))
            d.rectangle((x - 1, y - 1, x + tile, y + tile),
                        outline=(203, 210, 224) if bg is light else (70, 76, 88))
            x += tile + colgap
        y += tile + rowgap

    # 1:1 真实尺寸对照（左边浅底、右边深底）
    y0 = y
    d.text((pad, y0 - 4), "1:1 true size (same pixels, no zoom)", font=f15, fill=ink)
    y1 = y0 + 26
    d.rounded_rectangle((pad - 8, y1 - 8, pad + 520, y1 + 64), 8, fill=panel)
    x = pad + 8
    for size in sizes:
        for name in COMPARE:
            t = Image.new("RGBA", (size, size), light)
            t.alpha_composite(build_frame(name, size))
            out.alpha_composite(t, (x, y1 + 6))
            x += size + 10
    d.rounded_rectangle((pad + 540, y1 - 8, width - pad, y1 + 64), 8, fill=panel_dark)
    x = pad + 550
    for size in sizes:
        for name in COMPARE:
            t = Image.new("RGBA", (size, size), dark)
            t.alpha_composite(build_frame(name, size))
            out.alpha_composite(t, (x, y1 + 6))
            x += size + 10

    # 16px 合成到深色任务栏后的量化对比
    y2 = y1 + 78
    d.text((pad, y2), "16px composited on #202020 (dark taskbar):", font=f15, fill=ink)
    for i, name in enumerate(COMPARE):
        mean, std, contrast, levels = frame_stats(name, 16)
        d.text((pad, y2 + 22 + i * 20),
               "%s   contrast %.2f   luma %.1f   std %.1f   levels %d"
               % (name, contrast, mean, std, levels), font=f13, fill=muted)

    out.convert("RGB").save(path, format="PNG", optimize=True)


# ---------------------------------------------------------------- main
def main(argv):
    ap = argparse.ArgumentParser(description="生成 desktop-calendar 品牌图标")
    ap.add_argument("--pick", default=DEFAULT_PICK, choices=CANDIDATES,
                    help="写进 app.ico / logo-256 / app-32 / app-16 的候选（默认 cand-a）")
    args = ap.parse_args(argv)

    for name in CANDIDATES:
        # 256 用完整细节档；32 用该尺寸真正会出现的细节档，PNG 与 ico 帧一致
        build_frame(name, 256).save(os.path.join(HERE, "%s-256.png" % name),
                                    format="PNG", optimize=True)
        build_frame(name, 32).save(os.path.join(HERE, "%s-32.png" % name),
                                   format="PNG", optimize=True)

    write_ico(os.path.join(HERE, "app.ico"), args.pick, ICO_SIZES)
    build_frame(args.pick, 256).save(os.path.join(HERE, "logo-256.png"), format="PNG", optimize=True)
    build_frame(args.pick, 32).save(os.path.join(HERE, "app-32.png"), format="PNG", optimize=True)
    build_frame(args.pick, 16).save(os.path.join(HERE, "app-16.png"), format="PNG", optimize=True)
    make_preview(os.path.join(HERE, "icon-preview.png"), args.pick)
    make_small_compare(os.path.join(HERE, "small-compare.png"))

    # 自检输出（ASCII，避免控制台编码问题）
    for fn in sorted(os.listdir(HERE)):
        if fn.lower().endswith((".png", ".ico")):
            print("%-22s %8d B" % (fn, os.path.getsize(os.path.join(HERE, fn))))
    with Image.open(os.path.join(HERE, "app.ico")) as ico:
        print("app.ico frames: " + str(sorted(ico.ico.sizes())))
    print("16px on #202020:")
    for name in CANDIDATES:
        mean, std, contrast, levels = frame_stats(name, 16)
        print("  %-8s contrast %6.2f  luma %6.1f  std %6.2f  levels %d"
              % (name, contrast, mean, std, levels))
    import PIL
    print("pick=%s  pillow=%s  master=%dpx" % (args.pick, PIL.__version__, MASTER))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
