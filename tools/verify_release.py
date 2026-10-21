"""Read-only release verification; does not install or change Windows settings."""
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

root = Path(__file__).resolve().parents[1]
dist = root / 'dist'
portable = dist / 'Qivoryx-0.4.0-Portable'
version = '0.4.0'
archive = dist / f'Qivoryx-{version}-Portable-x64.zip'
hashes = json.loads((dist / 'SHA256.json').read_text(encoding='utf-8-sig'))
for item in hashes:
    assert hashlib.sha256((dist / item['File']).read_bytes()).hexdigest().upper() == item['Hash'].upper(), item['File']

with ZipFile(archive) as z:
    names = set(z.namelist())
    assert not any(n.lower().startswith('data/') for n in names), 'User data must never ship'
    assert not any('FSOS' in n or n.endswith('.apbx') for n in names), 'Raw Playbook must not ship'
    assert not any('analysis' in n.lower() or 'playbook' in n.lower() for n in names), 'Research reports must not ship'
    required = {'Qivoryx.exe', 'Backend.ps1', 'catalog.json', 'README.md', 'Modules/Performance.ps1', 'Modules/Tuning.ps1', 'Modules/Startup.ps1', 'Modules/Gpu.ps1', 'Modules/DefenderRemoval.ps1', 'Modules/AppRemoval.ps1'}
    assert required <= names, required - names
    for name in names:
        assert z.read(name) == (portable / name).read_bytes(), name
    count = len(names)
    readme = z.read('README.md').decode('utf-8-sig')
    assert not any(origin.casefold() in readme.casefold() for origin in ['FSOS', 'Igromanoff', 'Playbook', 'плейбук'])

verify_text = (root / 'artifacts/installer-verify-0.4.0.txt').read_text(encoding='utf-8-sig')
extract = Path(verify_text.split('Extraction: ', 1)[1].strip())
for path in extract.rglob('*'):
    if path.is_file():
        assert path.read_bytes() == (portable / path.relative_to(extract)).read_bytes(), str(path)
assert len([p for p in extract.rglob('*') if p.is_file()]) == count
checks = {name: len(json.loads((root / f'artifacts/{name}-tests.json').read_text(encoding='utf-8-sig'))) for name in ['backend', 'startup', 'performance', 'playbook', 'gpu', 'app-removal', 'defender-removal']}
frame = json.loads((root / 'artifacts/framelab-tests.json').read_text(encoding='utf-8-sig'))
assert frame['Passed'] and frame['Checks'] >= 43 and len(frame['TestNames']) == frame['Checks']
checks['framelab'] = frame['Checks']
sessions = json.loads((root / 'artifacts/gamesession-tests.json').read_text(encoding='utf-8-sig'))
assert sessions['Passed'] and sessions['Checks'] >= 46 and len(sessions['TestNames']) == sessions['Checks']
assert sessions['NativeMutations'] == 0 and sessions['RealGameLaunches'] == 0
checks['gameSessions'] = sessions['Checks']
assert 'All 32 page/theme' in (root / 'artifacts/ui/smoke-complete.txt').read_text(encoding='utf-8-sig')
ui = json.loads((root / 'artifacts/ui/ui-assertions.json').read_text(encoding='utf-8-sig'))
for mode in ['light', 'dark']:
    for page in range(16):
        assert (root / f'artifacts/ui/{mode}-{page}.png').is_file(), (mode, page)
report = {'product': 'Qivoryx', 'version': version, 'payloadFiles': count, 'zipMatchesPortable': True, 'setupPayloadMatchesPortable': True, 'packageHashesMatch': True, 'noUserDataOrResearchReports': True, 'backendChecks': checks, 'totalBackendChecks': sum(checks.values()), 'uiAssertions': len(ui), 'pageThemeCombinations': 32, 'mutationsDuringSmoke': False}
(root / 'artifacts/release-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
