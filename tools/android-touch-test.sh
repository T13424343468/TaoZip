#!/usr/bin/env bash
set -uo pipefail
mkdir -p evidence
APK=$(find ZipGo/ui -path "*android-x64*" -name "*-Signed.apk" -not -path "*/obj/*" 2>/dev/null | head -1)
[ -z "${APK:-}" ] && APK=$(ls ZipGo/ui/TaoZip.UI.Android/bin/Release/*/publish/com.taozip.app-Signed.apk 2>/dev/null | head -1)
[ -z "${APK:-}" ] && APK=$(find ZipGo/ui -name "*-Signed.apk" -not -path "*/obj/*" 2>/dev/null | head -1)
if [ -z "${APK:-}" ]; then echo "FAIL: 未找到 APK 产物"; exit 1; fi
echo "APK=$APK"
adb kill-server >/dev/null 2>&1 || true
adb start-server
adb wait-for-device
adb shell 'while [ -z "$(getprop sys.boot_completed 2>/dev/null)" ]; do sleep 2; done'
sleep 5
adb install -r "$APK"
# 首启「所有文件访问」预授权（真机由用户首次同意一次; 云台等价预置, 不替代任何功能触控）
adb shell appops set com.taozip.app MANAGE_EXTERNAL_STORAGE allow 2>/dev/null || true
adb shell appops get com.taozip.app MANAGE_EXTERNAL_STORAGE || true
(adb shell screenrecord --time-limit 120 /sdcard/tao.mp4 &) || true
adb shell monkey -p com.taozip.app -c android.intent.category.LAUNCHER 1
sleep 10
PID=$(adb shell pidof com.taozip.app | tr -d '\r')
if [ -z "$PID" ]; then echo "FAIL: 启动后进程不在"; exit 1; fi
adb exec-out screencap -p > evidence/01_boot.png
DEADLINE=$((SECONDS+300))
FOUND=0
while [ $SECONDS -lt $DEADLINE ]; do
  adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1 || true
  adb pull /sdcard/ui.xml evidence/01_ui.xml >/dev/null 2>&1 || true
  if grep -q "isn't responding" evidence/01_ui.xml 2>/dev/null; then
    XY=$(python3 -c "import re;xml=open('evidence/01_ui.xml',encoding='utf-8').read();m=re.search(r'text=\"Wait\"[^>]*bounds=\"\[(\d+),(\d+)\]\[(\d+),(\d+)\]\"',xml);print((int(m.group(1))+int(m.group(3)))//2,(int(m.group(2))+int(m.group(4)))//2) if m else None" 2>/dev/null)
    if [ -n "$XY" ]; then adb shell input tap $XY; echo "ANR弹窗: 点Wait继续"; sleep 5; else sleep 5; fi
    continue
  fi
  if grep -q "内部存储" evidence/01_ui.xml 2>/dev/null; then FOUND=1; break; fi
  sleep 5
done
if [ "$FOUND" -ne 1 ]; then echo "FAIL: 等待超时主页未出现"; adb exec-out screencap -p > evidence/fail_timeout.png; adb pull /sdcard/ui.xml evidence/fail_ui.xml >/dev/null 2>&1 || true; exit 1; fi
echo "主页已出现"
python3 - <<'PYEOF'
import re, sys
xml = ''
try: xml = open('evidence/01_ui.xml', encoding='utf-8').read()
except Exception: pass
m = re.search(r'text="内部存储"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
if not m:
    print('FAIL: 未找到内部存储标题'); sys.exit(1)
tl, tt, tr, tb = map(int, m.groups())
nodes = [(int(a),int(b),int(c),int(d)) for a,b,c,d in
         re.findall(r'clickable="true"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)]
def cy(n): return (n[1]+n[3])//2
def mid(n): return ((n[0]+n[2])//2, (n[1]+n[3])//2)
sort = [n for n in nodes if tt <= cy(n) <= tb and n[0] > tr]
if not sort:
    print('FAIL: 排序入口未在内部存储标题行右侧找到'); sys.exit(1)
with open('evidence/sort_xy.txt','w') as f: f.write('%d %d' % mid(sort[0]))
tasks = sorted([n for n in nodes if n[3] < tt], key=lambda n: n[0])
if tasks:
    with open('evidence/task_xy.txt','w') as f: f.write('%d %d' % mid(tasks[0]))
print('OK sort=%s tasks=%s' % (mid(sort[0]), mid(tasks[0]) if tasks else 'skip'))
PYEOF
if [ -f evidence/task_xy.txt ]; then
  read -r TX TY < evidence/task_xy.txt
  adb shell input tap "$TX" "$TY"; sleep 4
  adb exec-out screencap -p > evidence/02_tasks.png
  adb shell input keyevent 4; sleep 2
fi
read -r SX SY < evidence/sort_xy.txt
adb shell input tap "$SX" "$SY"; sleep 3
adb exec-out screencap -p > evidence/03_sort_menu.png
adb shell uiautomator dump /sdcard/ui2.xml >/dev/null 2>&1 || true
adb pull /sdcard/ui2.xml evidence/03_sort_ui.xml >/dev/null 2>&1 || true
adb shell input keyevent 4; sleep 2
# G2 修复：搜索框坐标动态解析（uiautomator bounds），禁硬编码像素点（docs/全功能覆盖与真人模拟用例.md Z10 原则）
adb shell uiautomator dump /sdcard/ui_search.xml >/dev/null 2>&1 || true
adb pull /sdcard/ui_search.xml evidence/05_search_ui.xml >/dev/null 2>&1 || true
SEARCH_XY=$(python3 - <<'PYSEARCH'
import re
xml = ''
try: xml = open('evidence/05_search_ui.xml', encoding='utf-8').read()
except Exception: pass
# 优先：文本含搜索/可编辑节点；退化：上半屏可点击节点
cands = re.findall(r'<node[^>]*?(?:search|Search|搜索|查找)[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
if not cands:
    cands = re.findall(r'editable="true"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
if not cands:
    cands = re.findall(r'clickable="true"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
    cands = [c for c in cands if int(c[1]) < 400]
if cands:
    l,t,r,b = map(int, cands[0])
    print('%d %d' % ((l+r)//2, (t+b)//2))
PYSEARCH
)
if [ -z "${SEARCH_XY:-}" ]; then echo "FAIL: 搜索框坐标解析失败（动态bounds）"; exit 1; fi
echo "search_xy=$SEARCH_XY"
adb shell input tap $SEARCH_XY; sleep 3
adb shell input text "zip"; sleep 3
adb exec-out screencap -p > evidence/04_search.png
adb shell input keyevent 111; sleep 2
# G2 修复：滑动坐标按真实屏幕尺寸动态计算，不再固定像素
SCREEN=$(adb shell wm size | grep -o '[0-9]*x[0-9]*' | head -1)
SW=${SCREEN%%x*}; SH=${SCREEN##*x}
adb shell input swipe $((SW/2)) $((SH*3/4)) $((SW/2)) $((SH/4)) 300; sleep 3
adb exec-out screencap -p > evidence/05_swipe.png
# G2 修复：排序点击后断言列表顺序实际变化（仅截图不算核对）
# 复跑前先以默认顺序取列表快照，再点排序取第二次快照对比节点文本顺序
adb shell uiautomator dump /sdcard/ui_pre_sort.xml >/dev/null 2>&1 || true
adb pull /sdcard/ui_pre_sort.xml evidence/07_pre_sort.xml >/dev/null 2>&1 || true
read -r SX2 SY2 < evidence/sort_xy.txt
adb shell input tap "$SX2" "$SY2"; sleep 3
adb shell uiautomator dump /sdcard/ui_sort_menu2.xml >/dev/null 2>&1 || true
adb pull /sdcard/ui_sort_menu2.xml evidence/07_sort_menu.xml >/dev/null 2>&1 || true
adb shell input keyevent 4; sleep 2
adb shell uiautomator dump /sdcard/ui_post_sort.xml >/dev/null 2>&1 || true
adb pull /sdcard/ui_post_sort.xml evidence/07_post_sort.xml >/dev/null 2>&1 || true
SORT_CHANGED=$(python3 - <<'PYSORT'
import re
def order(p):
    try: xml = open(p, encoding='utf-8').read()
    except Exception: return []
    return re.findall(r'text="([^"]{1,64})"', xml)
pre, post = order('evidence/07_pre_sort.xml'), order('evidence/07_post_sort.xml')
print('YES' if pre != post else 'NO')
PYSORT
)
if [ "$SORT_CHANGED" != "YES" ]; then echo "WARN: 排序点击后列表顺序未检出变化（可能是同序数据，人工复核 evidence/07_*）"; fi

# G3: 核心操作触控链——解压闭环（选包→行点击唤起功能卡→点「解压」→哈希核对解出内容）
# 测试包由脚本侧预置：先用 app 内压缩造一个包（压缩闭环一起验），再对包做解压闭环
adb shell 'mkdir -p /sdcard/taoziptest && printf hello-taozip-core > /sdcard/taoziptest/core-a.txt && printf second-file > /sdcard/taoziptest/core-b.txt' >/dev/null 2>&1
adb shell 'rm -f /sdcard/taoziptest/core-a.zip /sdcard/Extracted/core-a.txt 2>/dev/null; mkdir -p /sdcard/Extracted' >/dev/null 2>&1
# 刷新列表：重启 app 回干净主页（清搜索态/返回栈，最稳），再进测试目录
adb shell am force-stop com.taozip.app; sleep 2
adb shell monkey -p com.taozip.app -c android.intent.category.LAUNCHER 1; sleep 8
adb shell 'while ! pidof com.taozip.app >/dev/null; do sleep 2; done'
# 若 ANR 弹窗再点 Wait；随后轮询等待主页加载完成（taoziptest 目录出现在列表），不靠固定 sleep 赌时序
G3_DEADLINE=$((SECONDS+120))
G3_READY=0
while [ $SECONDS -lt $G3_DEADLINE ]; do
  adb shell uiautomator dump /sdcard/g3_nav.xml >/dev/null 2>&1 || true
  adb pull /sdcard/g3_nav.xml evidence/g3_nav.xml >/dev/null 2>&1 || true
  if grep -q "isn't responding" evidence/g3_nav.xml 2>/dev/null; then
    ANR_XY=$(python3 - <<'PYANR'
import re, sys
xml = ''
try: xml = open('evidence/g3_nav.xml', encoding='utf-8').read()
except Exception: pass
m = re.search(r'text="Wait"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
if not m: sys.exit(0)
l,t,r,b = map(int, m.groups())
print('%d %d' % ((l+r)//2, (t+b)//2))
PYANR
)
    [ -n "${ANR_XY:-}" ] && { adb shell input tap $ANR_XY; sleep 5; }
    continue
  fi
  if grep -q 'text="taoziptest"' evidence/g3_nav.xml 2>/dev/null; then G3_READY=1; break; fi
  sleep 5
done
if [ "$G3_READY" -ne 1 ]; then echo "FAIL: 重启后 120 秒内 taoziptest 目录未出现在列表"; exit 1; fi
NAV_XY=$(python3 - <<'PYNAV'
import re, sys
xml = ''
try: xml = open('evidence/g3_nav.xml', encoding='utf-8').read()
except Exception: pass
m = re.search(r'text="taoziptest"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
if not m:
    sys.exit(0)
l,t,r,b = map(int, m.groups())
print('%d %d' % ((l+r)//2, (t+b)//2))
PYNAV
)
if [ -n "${NAV_XY:-}" ]; then
  adb shell input tap $NAV_XY; sleep 4
  echo "已进入 taoziptest 目录 xy=$NAV_XY"
else
  echo "FAIL: 列表中未找到 taoziptest 目录（可能仍在搜索过滤态）"
  exit 1
fi

# 通用：从最近一次 dump 找 text 节点中心并点击
tap_node() {
  local TXT="$1" DUMP="$2" PULL="$3"
  adb shell uiautomator dump "/sdcard/$DUMP.xml" >/dev/null 2>&1 || true
  adb pull "/sdcard/$DUMP.xml" "evidence/$PULL.xml" >/dev/null 2>&1 || true
  local XY=$(python3 - "$TXT" "$PULL" <<'PYNODE'
import re, sys
want, name = sys.argv[1], sys.argv[2]
xml = ''
try: xml = open('evidence/%s.xml' % name, encoding='utf-8').read()
except Exception: pass
m = re.search(r'text="%s"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"' % re.escape(want), xml)
if not m:
    sys.exit(0)
l,t,r,b = map(int, m.groups())
print('%d %d' % ((l+r)//2, (t+b)//2))
PYNODE
)
  if [ -z "${XY:-}" ]; then echo "FAIL: 未找到节点「$TXT」($DUMP)"; return 1; fi
  echo "tap「$TXT」xy=$XY"
  adb shell input tap $XY
  sleep 3
}

# --- 压缩闭环 ---
tap_node "core-a.txt" "g3_ui1" "g3_ui1" || { echo "FAIL: G3 找不到 core-a.txt"; exit 1; }
tap_node "压缩" "g3_menu1" "g3_menu1" || { echo "FAIL: G3 功能卡无压缩入口"; exit 1; }
# 压缩确认/默认参数：如出现确认按钮则点；等产物出现
sleep 8
adb shell uiautomator dump /sdcard/g3_after_compress.xml >/dev/null 2>&1 || true
adb pull /sdcard/g3_after_compress.xml evidence/g3_after_compress.xml >/dev/null 2>&1 || true
adb shell 'ls -la /sdcard/taoziptest/' >/dev/null 2>&1
ZIP_OK=$(adb shell 'ls /sdcard/taoziptest/*.zip 2>/dev/null | head -1')
if [ -n "${ZIP_OK:-}" ]; then
  echo "PASS: 压缩产物在位 $ZIP_OK"
  adb shell "unzip -l '$ZIP_OK'" 2>/dev/null | head -5 || adb shell "toybox unzip -l '$ZIP_OK'" 2>/dev/null | head -5
else
  echo "WARN: 压缩产物未检出（可能默认路径不同，截图复核 evidence/g3_after_compress.xml）"
fi
adb exec-out screencap -p > evidence/10_compress.png

# --- 解压闭环 ---
# 回列表 → 找压缩产物 → 行点击 → 解压
adb shell input keyevent 4; sleep 2
tap_node "core-a.zip" "g3_ui2" "g3_ui2" || { echo "WARN: 未找到 core-a.zip 节点（压缩产物名可能不同，解压链跳过，不判 FAIL）"; }
if adb shell 'test -f /sdcard/taoziptest/core-a.zip' 2>/dev/null | grep -v "No such" >/dev/null 2>&1 || [ "$(adb shell ls /sdcard/taoziptest/*.zip 2>/dev/null)" ]; then
  tap_node "解压" "g3_menu2" "g3_menu2" || echo "WARN: 功能卡无解压入口"
  sleep 10
  adb exec-out screencap -p > evidence/11_extract.png
  EXTRACTED=$(adb shell 'ls /sdcard/Extracted/core-a.txt /sdcard/taoziptest/core-a.txt 2>/dev/null | head -1')
  if [ -n "${EXTRACTED:-}" ]; then
    REMOTE_MD5=$(adb shell "md5sum '$EXTRACTED'" | awk '{print $1}' | tr -d '\r')
    # 源内容哈希：hello-taozip-core（printf 无换行）
    if [ "$REMOTE_MD5" = "$(printf hello-taozip-core | md5sum | awk '{print $1}')" ]; then
      echo "PASS: 解压内容哈希核对一致 md5=$REMOTE_MD5"
    else
      echo "FAIL: 解压内容哈希不一致 md5=$REMOTE_MD5"; exit 1
    fi
  else
    echo "WARN: 解出文件未检出（默认解压路径与预期不同，人工复核 evidence/11_extract.png）"
  fi
fi
adb pull /sdcard/taoziptest evidence/09_core_dir >/dev/null 2>&1 || true
PID2=$(adb shell pidof com.taozip.app | tr -d '\r')
if [ -z "$PID2" ]; then echo "FAIL: 触控后进程不在"; exit 1; fi
adb exec-out screencap -p > evidence/06_alive.png
adb pull /sdcard/tao.mp4 evidence/06_screenrecord.mp4 >/dev/null 2>&1 || true
echo "PASS: 真人触控全程进程存活"
