#!/usr/bin/env python3
"""TaoZip 图标生成：太极 + 拉链齿环，Ant Design 蓝调。
输出 PC 多尺寸 PNG/ICO 与安卓自适应图标前景/背景层。
"""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__))
BLUE = (22, 119, 255, 255)      # Ant Design #1677FF
BLUE_DEEP = (9, 88, 217, 255)   # #0958D9
WHITE = (255, 255, 255, 255)
MIST = (187, 224, 255, 255)     # #BAE0FF 拉链齿
TRANS = (0, 0, 0, 0)

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))

def gradient_bg(size):
    """垂直渐变圆角方块背景。"""
    img = Image.new("RGBA", (size, size), TRANS)
    g = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(g)
    top = lerp(BLUE, (77, 157, 255, 255), 0.45)
    for y in range(size):
        gd.line([(0, y), (size, y)], fill=lerp(top, BLUE_DEEP, y / size))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1],
                                           radius=int(size * 0.223), fill=255)
    img.paste(g, (0, 0), mask)
    return img

def draw_taiji(draw, cx, cy, r, yang, yin):
    """标准太极：yang=阳鱼色（右下白），yin=阴鱼色（左上深蓝）。
    S 线由：大圆(阳) + 左半圆(阴) + 上小圆(阴) + 下小圆(阳) 组成。"""
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=yang)          # 底圆=阳
    draw.pieslice([cx - r, cy - r, cx + r, cy + r], 90, 270, fill=yin) # 左半=阴
    draw.ellipse([cx - r // 2, cy - r, cx + r // 2, cy], fill=yin)     # 上小圆=阴
    draw.ellipse([cx - r // 2, cy, cx + r // 2, cy + r], fill=yang)    # 下小圆=阳
    eye = r * 0.14
    draw.ellipse([cx - eye, cy - r // 2 - eye, cx + eye, cy - r // 2 + eye], fill=yang)
    draw.ellipse([cx - eye, cy + r // 2 - eye, cx + eye, cy + r // 2 + eye], fill=yin)

def make_icon(size, with_bg=True):
    img = gradient_bg(size) if with_bg else Image.new("RGBA", (size, size), TRANS)
    d = ImageDraw.Draw(img)
    cx = cy = size / 2

    # ---- 拉链齿环：交替齿（白/浅蓝），像环绕的拉链 ----
    ring_r = size * 0.43
    thickness = size * 0.045
    n = 36
    box = [cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r]
    for i in range(n):
        a0 = i * 360 / n
        a1 = a0 + 360 / n * 0.55
        d.pieslice(box, a0, a1, fill=WHITE if i % 2 == 0 else MIST)
    # 齿环内缘收齐
    inner = ring_r - thickness
    d.ellipse([cx - inner, cy - inner, cx + inner, cy + inner], fill=BLUE_DEEP)

    # ---- 太极 ----
    tr = inner * 0.97
    draw_taiji(d, cx, cy, tr, yang=WHITE, yin=BLUE_DEEP)

    # ---- 拉链头：替换上鱼眼（深蓝鱼身配白色拉链头，清晰可见） ----
    ey = cy - tr / 2
    head_w, head_h = tr * 0.34, tr * 0.22
    d.rounded_rectangle([cx - head_w / 2, ey - head_h / 2, cx + head_w / 2, ey + head_h / 2],
                        radius=head_h * 0.4, fill=WHITE)
    pull_r = tr * 0.085
    ly = ey - head_h / 2 - pull_r * 1.7
    d.ellipse([cx - pull_r, ly - pull_r, cx + pull_r, ly + pull_r],
              outline=WHITE, width=max(2, int(size * 0.014)))
    return img

def make_foreground(size):
    """安卓自适应图标前景：透明底，主体缩至 66% 安全区。"""
    base = make_icon(size, with_bg=False)
    body = int(size * 0.66)
    scaled = base.resize((body, body), Image.LANCZOS)
    img = Image.new("RGBA", (size, size), TRANS)
    off = (size - body) // 2
    img.paste(scaled, (off, off), scaled)
    return img

def main():
    for s in (16, 24, 32, 48, 64, 128, 256, 512, 1024):
        make_icon(s).save(os.path.join(OUT, f"logo_{s}.png"))
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [make_icon(s) for s in ico_sizes]
    imgs[-1].save(os.path.join(OUT, "TaoZip.ico"), format="ICO",
                  sizes=[(s, s) for s in ico_sizes])
    make_foreground(432).save(os.path.join(OUT, "android_fg.png"))
    gradient_bg(432).save(os.path.join(OUT, "android_bg.png"))
    make_icon(512).save(os.path.join(OUT, "app_icon.png"))
    print("icons done")

if __name__ == "__main__":
    main()
