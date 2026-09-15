#!/usr/bin/env zsh
# Run EditMode tests through the Unity CLI and fail unless a fresh report shows zero failures.
# usage: tools/test.sh   (exit 0 only when the run succeeded and failures == errors == 0)
set -uo pipefail
cd "$(dirname "$0")/.."
export UNITY_NON_INTERACTIVE=1 UNITY_NO_BANNER=1
unity license status 2>&1 | head -1 | grep -q active || unity license activate --personal --accept-eula >/dev/null 2>&1
rm -f TestResults/editmode.xml
out=$(unity test . --mode EditMode --report-format junit --output TestResults/editmode.xml --timeout 900 --format json 2>&1)
ok=$(printf '%s' "$out" | python3 -c "
import sys,json
last=None
for line in sys.stdin:
    line=line.strip()
    if line.startswith('{') and '\"command\"' in line and '\"success\"' in line:
        try: last=json.loads(line)
        except Exception: pass
print('1' if last and last.get('success') else '0')
if last and not last.get('success'): print(json.dumps(last.get('errors'))[:300], file=sys.stderr)
")
if [ ! -f TestResults/editmode.xml ]; then echo "test: no report written (cli success=$ok)"; exit 1; fi
python3 - <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse('TestResults/editmode.xml').getroot()
tests, fails, errs = int(r.get('tests') or 0), int(r.get('failures') or 0), int(r.get('errors') or 0)
print(f"tests={tests} failures={fails} errors={errs}")
for tc in r.iter('testcase'):
    f = tc.find('failure') or tc.find('error')
    if f is not None:
        print('FAIL', tc.get('classname'), tc.get('name'), (f.get('message') or '')[:300])
sys.exit(0 if fails == 0 and errs == 0 and tests > 0 else 1)
PY
