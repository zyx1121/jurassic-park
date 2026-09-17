#!/usr/bin/env zsh
# Run EditMode tests through the Unity CLI and fail unless a fresh report shows zero failures.
# usage: tools/test.sh [EditMode|PlayMode]   (default EditMode; exit 0 only when the run succeeded and failures == errors == 0)
set -uo pipefail
cd "$(dirname "$0")/.."
mode=${1:-EditMode}
report="TestResults/$(printf %s "$mode" | tr "[:upper:]" "[:lower:]").xml"
export PATH="$HOME/.unity/bin:$PATH" UNITY_NON_INTERACTIVE=1 UNITY_NO_BANNER=1
unity license status 2>&1 | head -1 | grep -q active || unity license activate --personal --accept-eula >/dev/null 2>&1
rm -f $report
out=$(unity test . --mode $mode --report-format junit --output $report --timeout 900 --format json 2>&1)
ok=$(printf '%s' "$out" | python3 -c "
import sys,json
text=sys.stdin.read()
# the final envelope is pretty-printed; take the last top-level JSON object
last=None
depth=0; start=None
for i,ch in enumerate(text):
    if ch=='{':
        if depth==0: start=i
        depth+=1
    elif ch=='}':
        depth-=1
        if depth==0 and start is not None:
            try:
                obj=json.loads(text[start:i+1])
                if obj.get('command')=='test' and 'success' in obj: last=obj
            except Exception: pass
print('1' if last and last.get('success') else '0')
if last and not last.get('success'): print(json.dumps(last.get('errors'))[:300], file=sys.stderr)
")
if [ ! -f $report ]; then
  # Usually a compile error: the Editor exits before the run starts and the CLI only says "did not complete".
  elog="$HOME/Library/Logs/Unity/Editor.log"
  [ -f "$elog" ] && grep -E "error CS[0-9]+" "$elog" | sort -u | head -20
  echo "test: no report written (cli success=$ok)"; exit 1
fi
python3 - "$report" <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
tests, fails, errs = int(r.get('tests') or 0), int(r.get('failures') or 0), int(r.get('errors') or 0)
print(f"tests={tests} failures={fails} errors={errs}")
for tc in r.iter('testcase'):
    f = tc.find('failure')
    if f is None: f = tc.find('error')
    if f is not None:
        print('FAIL', tc.get('classname'), tc.get('name'), (f.get('message') or '')[:300])
sys.exit(0 if fails == 0 and errs == 0 and tests > 0 else 1)
PY
