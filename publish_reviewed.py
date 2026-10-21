"""One-time, scoped publication of reviewed packages. Run only in this repo's CI.

No ROM inputs or reconstruction run on GitHub. Only source, licenses and the
explicitly reviewed distribution ZIPs enter CI. Old release/tag names are fixed.
"""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import zipfile

REPO = 'ayaneros-eng/kaze-no-regret-dreamcast-korean-patch'
NEW = ('v1.5.0', 'editor-v1.0.1')
OLD = ('v1.4.0', 'editor-v1.0.0')


def run(*args, data=None):
    p = subprocess.run(args, input=data, text=True, capture_output=True, check=True)
    return p.stdout.strip()


def api(path):
    return json.loads(run('gh', 'api', f'repos/{REPO}/{path}'))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    assert os.environ.get('GITHUB_REPOSITORY') == REPO
    assert os.environ.get('GITHUB_EVENT_NAME') == 'push'
    head = os.environ['GITHUB_SHA']
    assert api('git/ref/heads/main')['object']['sha'] == head
    refs = api('git/matching-refs/')
    required={'refs/heads/main', *(f'refs/tags/{t}' for t in OLD)}
    optional={f'refs/tags/{t}' for t in NEW}
    assert required <= {r['ref'] for r in refs} <= required | optional
    old_refs = {r['ref']: r['object']['sha'] for r in refs if r['ref'] in {f'refs/tags/{t}' for t in OLD}}
    new_refs = {r['ref']: r['object']['sha'] for r in refs if r['ref'] in optional}
    assert not new_refs or len(new_refs)==2 and len(set(new_refs.values()))==1
    releases={r['tag_name']:r for r in api('releases?per_page=100')}
    assert set(OLD) <= releases.keys() <= set(OLD+NEW)
    assert all(releases[t]['draft'] for t in NEW if t in releases)
    old_releases = [releases[t] for t in OLD]
    assert {r['tag_name'] for r in old_releases} == set(OLD)

    dist = Path('dist')
    expected = json.loads((dist/'UPLOAD-MANIFEST.json').read_text(encoding='utf-8'))
    for name, checksum in expected.items():
        assert '/' not in name and '\\' not in name
        assert digest(dist/name) == checksum
    assert {p.name for p in dist.iterdir()} == {*expected, 'UPLOAD-MANIFEST.json'}
    for name in expected:
        if name.endswith('.zip'):
            with zipfile.ZipFile(dist/name) as z:
                assert z.testzip() is None
                assert len(z.namelist()) == len(set(z.namelist()))
                assert not any(n.lower().endswith(('.chd','.gdi','.cue','.bin','.raw','.iso','.cdi','.bps')) for n in z.namelist())

    # Remove only the reviewed upload staging directory from the new source tree.
    run('git','rm','-r','--cached','dist')
    tracked = run('git','ls-files','-z').split('\x00')
    tracked = [p for p in tracked if p and p != 'SHA256SUMS.txt']
    manifest = {p: {'bytes':Path(p).stat().st_size,'sha256':digest(p)} for p in tracked}
    source = dist/'KazeTools_v1.5_Source.zip'
    with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for p in sorted(tracked):
            info=zipfile.ZipInfo(p,(2026,10,21,0,0,0)); info.compress_type=zipfile.ZIP_DEFLATED
            z.writestr(info,Path(p).read_bytes(),compresslevel=9)
        z.writestr('SOURCE-MANIFEST.json',json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    with zipfile.ZipFile(source) as z:
        assert z.testzip() is None
        for p,meta in manifest.items():
            b=z.read(p); assert len(b)==meta['bytes'] and hashlib.sha256(b).hexdigest()==meta['sha256']
    sums = dist/'RELEASE-SHA256SUMS.txt'
    assets = [dist/n for n in expected if n.endswith('.zip')] + [source]
    assets += [Path('disc1.kzrp'),Path('disc2.kzrp')]
    sums.write_text(''.join(f'{digest(p)}  {p.name}\n' for p in assets),encoding='utf-8')

    # commit-tree without -p creates a clean root with no legacy parent history.
    env = dict(os.environ,GIT_AUTHOR_NAME='ayaneros-eng',GIT_COMMITTER_NAME='ayaneros-eng',
               GIT_AUTHOR_EMAIL='228732975+ayaneros-eng@users.noreply.github.com',
               GIT_COMMITTER_EMAIL='228732975+ayaneros-eng@users.noreply.github.com')
    tree = run('git','write-tree')
    root = subprocess.run(['git','commit-tree',tree,'-m','Release v1.5 source-dependent distribution and editor 1.0.1'],env=env,capture_output=True,text=True,check=True).stdout.strip()
    assert not run('git','show','-s','--format=%P',root)
    tag_leases=[f'--force-with-lease={ref}:{sha}' for ref,sha in new_refs.items()]
    run('git','push','--atomic',*tag_leases,'origin',*(f'{root}:refs/tags/{t}' for t in NEW))

    patchzip=dist/'KazeNoRegret_DC_Korean_v1.5_Windows.zip'
    editorzip=dist/'KazeSubtitleEditor_v1.0.1_Windows.zip'
    def draft(tag, files, title, notes):
        if tag in releases:
            assert {a['name'] for a in releases[tag]['assets']}=={Path(p).name for p in files}
            run('gh','release','upload',tag,*files,'--repo',REPO,'--clobber')
        else:
            run('gh','release','create',tag,*files,'--repo',REPO,'--verify-tag','--draft','--title',title,'--notes-file',notes)
    draft(NEW[0],[str(patchzip),str(source),'disc1.kzrp','disc2.kzrp',str(sums)],
          'v1.5 — 원본 ROM 참조 방식 간편 한글패치','release-notes-v1.5.md')
    draft(NEW[1],[str(editorzip),str(sums)],'자막 편집기 1.0.1 — 라이선스 고지 보강','release-notes-editor.md')
    releases={r['tag_name']:r for r in api('releases?per_page=100')}
    for tag in NEW:
        for asset in api(f"releases/{releases[tag]['id']}")['assets']:
            local = next(p for p in [patchzip,editorzip,source,sums,Path('disc1.kzrp'),Path('disc2.kzrp')] if p.name==asset['name'])
            assert asset['size']==local.stat().st_size
            assert asset.get('digest')=='sha256:'+digest(local)

    leases = [f'--force-with-lease=refs/heads/main:{head}'] + [f'--force-with-lease={ref}:{sha}' for ref,sha in old_refs.items()]
    run('git','push','--atomic',*leases,'origin',f'{root}:refs/heads/main',*(f':{ref}' for ref in old_refs))
    for release in old_releases:
        # Recheck the exact release before deleting; no other releases are touched.
        current=api(f"releases/{release['id']}")
        assert current['tag_name']==release['tag_name']
        run('gh','api','--method','DELETE',f"repos/{REPO}/releases/{release['id']}")
    run('gh','release','edit',NEW[1],'--repo',REPO,'--draft=false','--latest=false')
    run('gh','release','edit',NEW[0],'--repo',REPO,'--draft=false','--latest=true')
    assert {r['ref'] for r in api('git/matching-refs/')} == {'refs/heads/main',*(f'refs/tags/{t}' for t in NEW)}
    assert {r['tag_name'] for r in api('releases?per_page=100')} == set(NEW)
    assert len(api('commits?per_page=100'))==1
    print(json.dumps(dict(clean_root_commit=root,public_releases=list(NEW),old_releases_deleted=list(OLD),all_asset_digests_verified=True),indent=2))


if __name__ == '__main__':
    main()
