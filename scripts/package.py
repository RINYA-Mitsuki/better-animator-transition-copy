"""Validate and deterministically build one VPM package (Python standard library)."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

SEMVER = re.compile(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?')

def validate_version(version):
    match = SEMVER.fullmatch(version)
    if not match or (match[4] and any(p.isdigit() and len(p)>1 and p[0]=='0' for p in match[4].split('.'))):
        raise ValueError('Invalid release SemVer (build metadata is not used): ' + version)

def validate_manifest(m):
    for key in ('name','version','displayName','author','url','unity','license'):
        if not m.get(key): raise ValueError('Missing manifest field: ' + key)
    if not re.fullmatch(r'[a-z0-9]+(?:[.-][a-z0-9]+)+',m['name']): raise ValueError('Invalid package ID')
    validate_version(m['version'])
    if not m['author'].get('name'): raise ValueError('Missing author name')
    if m['license'] == 'UNLICENSED': raise ValueError('Choose distribution license before release')
    if 'zipSHA256' in m: raise ValueError('zipSHA256 belongs only in the listing')
    if not m['url'].startswith('https://github.com/'): raise ValueError('Expected GitHub HTTPS asset URL')
    if m.get('legacyFolders') or m.get('legacyFiles'): raise ValueError('Automatic legacy deletion requires separately reviewed migration')

def build(root, out, repository=None):
    root, out = Path(root), Path(out)
    m=json.loads((root/'package.json').read_text(encoding='utf-8-sig'))
    validate_manifest(m)
    filename=f"{m['name']}-{m['version']}.zip"
    if root.name != m['name']: raise ValueError('Package folder and ID differ')
    if repository and m['url'] != f"https://github.com/{repository}/releases/download/v{m['version']}/{filename}":
        raise ValueError('Manifest URL does not match repository/version')
    for required in ('README.md','CHANGELOG.md','LICENSE.md','Editor'):
        if not (root/required).exists(): raise ValueError('Missing: '+required)
    guids=set()
    for p in root.rglob('*'):
        if p.is_symlink(): raise ValueError('Symlinks cannot be packaged')
        if p.name.startswith('.') or p.name in ('Library','Temp','__pycache__'): raise ValueError('Unexpected package file: '+str(p))
        if p.suffix != '.meta' and not Path(str(p)+'.meta').is_file(): raise ValueError('Missing meta: '+str(p))
        if p.suffix == '.meta':
            if not Path(str(p)[:-5]).exists(): raise ValueError('Orphan meta: '+str(p))
            matches=re.findall(r'^guid: ([a-f0-9]{32})$',p.read_text(),re.M)
            if len(matches)!=1 or matches[0] in guids: raise ValueError('Invalid/duplicate GUID: '+str(p))
            guids.add(matches[0])
        if p.suffix == '.cs' and 'Editor' not in p.relative_to(root).parts: raise ValueError('Editor-only package contains runtime script')
        if p.suffix == '.asmdef':
            asm=json.loads(p.read_text())
            if asm.get('includePlatforms') != ['Editor']: raise ValueError('Assembly must be Editor only')
    if not list((root/'Editor').glob('*.asmdef')): raise ValueError('Missing Editor assembly definition')
    out.mkdir(parents=True,exist_ok=True)
    archive=out/filename
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for p in sorted(root.rglob('*')):
            if p.is_file():
                info=zipfile.ZipInfo(p.relative_to(root).as_posix(),date_time=(2020,1,1,0,0,0))
                info.compress_type=zipfile.ZIP_DEFLATED
                info.external_attr=0o100644<<16
                z.writestr(info,p.read_bytes())
    (out/'package.json').write_bytes((root/'package.json').read_bytes())
    digest=hashlib.sha256(archive.read_bytes()).hexdigest()
    (out/'SHA256SUMS').write_text(f'{digest}  {filename}\n',encoding='utf-8')
    return m,archive,digest

if __name__ == '__main__':
    p=argparse.ArgumentParser()
    p.add_argument('package');p.add_argument('--out',default='dist');p.add_argument('--repository',required=True)
    a=p.parse_args()
    m,z,d=build(a.package,a.out,a.repository)
    print(f"Validated {m['name']} {m['version']}: {z.name} SHA256={d}")
