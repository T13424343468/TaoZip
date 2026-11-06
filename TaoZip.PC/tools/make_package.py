# TaoZip PC 交付包打包脚本 (v91)
# 规则: 包内根前缀 TaoZip.PC/; 全 ASCII 路径; 排除 bin/obj/bin-check/.vs/.git 与 zip 残留;
#       写完后逐条改头部通用标志位, 全条目置 UTF-8 标志 0x800
import os, sys, struct, zipfile

# ROOT 必须是 TaoZip.PC/ 本身 (本脚本在 tools/ 下, 往上两级); v88 修正: 原写法把 tools 当成了根,
# 打出的包只有 tools 子树 47 项且落在树内, 主程序源码全部缺席——交付前必须抽验包内代码内容
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(os.path.dirname(ROOT), "TaoZip-PC-Full-v91.zip")
EXCLUDE_DIRS = {"bin", "obj", "bin-check", ".vs", ".git", ".idea"}

entries = []
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
    for name in filenames:
        full = os.path.join(dirpath, name)
        if name.lower().endswith(".zip") or os.path.abspath(full) == os.path.abspath(__file__):
            continue
        rel = "TaoZip.PC/" + os.path.relpath(full, ROOT).replace(os.sep, "/")
        if not rel.isascii():
            print("NON-ASCII PATH:", rel); sys.exit(1)
        entries.append((full, rel))

with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for full, rel in sorted(entries, key=lambda e: e[1]):
        info = zipfile.ZipInfo(rel)
        info.external_attr = 0o644 << 16
        with open(full, "rb") as f:
            z.writestr(info, f.read(), zipfile.ZIP_DEFLATED)

# 精确置位: 按 zipfile 记录的偏移改本地头 (+6) 与中央目录头 (+8) 的通用标志位
src = zipfile.ZipFile(OUT)
offsets = [i.header_offset for i in src.infolist()]
data = bytearray(open(OUT, "rb").read())
n_local = 0
for off in offsets:
    assert bytes(data[off:off+4]) == b"PK\x03\x04"
    flag = struct.unpack_from("<H", data, off + 6)[0]
    struct.pack_into("<H", data, off + 6, flag | 0x800)
    n_local += 1
# 中央目录起点: 从 EOCD (PK\x05\x06) 取偏移
eocd = data.rfind(b"PK\x05\x06")
cd_off, cd_count = struct.unpack_from("<I", data, eocd + 16)[0], struct.unpack_from("<H", data, eocd + 10)[0]
off = cd_off
n_central = 0
for _ in range(cd_count):
    assert bytes(data[off:off+4]) == b"PK\x01\x02"
    flag = struct.unpack_from("<H", data, off + 8)[0]
    struct.pack_into("<H", data, off + 8, flag | 0x800)
    n_central += 1
    off += 46 + struct.unpack_from("<H", data, off + 28)[0] + struct.unpack_from("<H", data, off + 30)[0] + struct.unpack_from("<H", data, off + 32)[0]
src.close()
open(OUT, "wb").write(data)

with zipfile.ZipFile(OUT) as z:
    names = z.namelist()
    assert len(names) == len(entries), (len(names), len(entries))
    assert n_local == len(entries) and n_central == len(entries), (n_local, n_central)
    assert all(i.flag_bits & 0x800 for i in z.infolist())
    print(f"entries={len(names)} utf8_flag_all=True size={os.path.getsize(OUT)}")
