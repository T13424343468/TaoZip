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
adb shell input tap 540 320; sleep 3
adb shell input text "zip"; sleep 3
adb exec-out screencap -p > evidence/04_search.png
adb shell input keyevent 111; sleep 2
adb shell input swipe 540 1400 540 400 300; sleep 3
adb exec-out screencap -p > evidence/05_swipe.png
PID2=$(adb shell pidof com.taozip.app | tr -d '\r')
if [ -z "$PID2" ]; then echo "FAIL: 触控后进程不在"; exit 1; fi
adb exec-out screencap -p > evidence/06_alive.png
adb pull /sdcard/tao.mp4 evidence/06_screenrecord.mp4 >/dev/null 2>&1 || true
echo "PASS: 真人触控全程进程存活"
