"""Publish a verified draft release; retries never overwrite existing asset bytes."""
import json, os, subprocess, tempfile, time
from pathlib import Path
from package import build

def gh(*args, **kwargs):
    result=subprocess.run(['gh',*args],capture_output=True,**kwargs)
    if result.returncode:
        raise RuntimeError(result.stderr.decode('utf-8',errors='replace'))
    return result.stdout

def find_release(repo,tag):
    pages=json.loads(gh('api','--paginate','--slurp',f'repos/{repo}/releases?per_page=100'))
    return next((r for page in pages for r in page if r['tag_name']==tag),None)

def main():
    repo=os.environ['GITHUB_REPOSITORY']
    root=next(Path('Packages').glob('*/package.json')).parent
    m,archive,digest=build(root,'dist',repo)
    tag='v'+m['version']
    ref=os.environ.get('GITHUB_REF','')
    if ref not in ('refs/heads/main','refs/tags/'+tag): raise ValueError('Release only from main or matching version tag')
    head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
    tags=json.loads(gh('api',f'repos/{repo}/git/matching-refs/tags/{tag}'))
    exact=[t for t in tags if t['ref']=='refs/tags/'+tag]
    if exact:
        subprocess.run(['git','fetch','origin','tag',tag],check=True)
        tagged=subprocess.check_output(['git','rev-parse',tag+'^{commit}'],text=True).strip()
        if tagged!=head: raise ValueError('Existing version tag points at a different commit; bump version')
    release=find_release(repo,tag)
    if release is None:
        gh('release','create',tag,'--repo',repo,'--target',head,'--draft','--title',f"{m['displayName']} {m['version']}",
           '--notes-file',str(root/'CHANGELOG.md'))
        for _ in range(10):
            release=find_release(repo,tag)
            if release is not None:
                break
            time.sleep(1)
        if release is None: raise ValueError('Created draft release was not returned by API')
    if release['draft'] and not exact:
        gh('release','edit',tag,'--repo',repo,'--target',head)
    assets={a['name']:a for a in release['assets']}
    for path in (archive,Path('dist/package.json'),Path('dist/SHA256SUMS')):
        if path.name in assets:
            data=gh('api',f"repos/{repo}/releases/assets/{assets[path.name]['id']}",'-H','Accept: application/octet-stream')
            if data!=path.read_bytes(): raise ValueError('Existing asset differs: '+path.name)
        elif release['draft']:
            gh('release','upload',tag,str(path),'--repo',repo)
        else:
            raise ValueError('Published release missing asset; refusing mutation')
    if release['draft']:
        gh('release','edit',tag,'--repo',repo,'--draft=false','--prerelease='+str('-' in m['version']).lower())
    print(f'Published/verified {repo} {tag}; SHA256={digest}')

if __name__=='__main__': main()
