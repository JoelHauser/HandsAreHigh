# Checks a cfg migrated by tools/cfgbind/bind.ps1 against the Moves table in Settings.Layout.cs:
# every old (section, key) that had a value must have the same value under its new (section, key),
# and no old line may be left. Usage (after bind.ps1 on a COPY):
#   python -I tools/cfgbind/carry.py <original cfg copy> <work>/joel.cfg
import re, sys, os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LAYOUT = os.path.join(ROOT, 'src', 'HandsAreHigh', 'Settings.Layout.cs')


def read(path):
    m, sec = {}, None
    for line in open(path, encoding='utf-8-sig').read().splitlines():
        g = re.match(r'^\[(.+)\]$', line)
        if g:
            sec = g.group(1)
            continue
        g = re.match(r'^([^#=][^=]*?) = (.*)$', line)
        if g:
            m[(sec, g.group(1))] = g.group(2)
    return m


moves = re.findall(r'new\[\] \{ "([^"]*)", "([^"]*)", "([^"]*)", "([^"]*)" \}', open(LAYOUT, encoding='utf-8').read())
before, after = read(sys.argv[1]), read(sys.argv[2])
checked = problems = 0
for a, b, c, d in moves:
    if (a, b) not in before:
        continue
    checked += 1
    if after.get((c, d)) != before[(a, b)]:
        problems += 1
        print(f'MISMATCH [{a}] {b} = {before[(a, b)]}  ->  [{c}] {d} = {after.get((c, d))}')
    if (a, b) in after:
        problems += 1
        print(f'OLD LINE LEFT [{a}] {b}')
old_sections = {a for a, *_ in moves}
left = sorted({s for (s, _k) in after if s in old_sections})
if left:
    problems += 1
    print('old sections still in the file:', left)
print(f'{len(moves)} moves in the table, {checked} values present before, {problems} problem(s)')
sys.exit(1 if problems else 0)
