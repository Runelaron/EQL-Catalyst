"""Package an already built Linux preview. Does not publish or modify the host."""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, tarfile, tempfile
import xml.etree.ElementTree as ET
from package_policy import assert_tree, assert_names

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--payload',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    root=Path(__file__).resolve().parents[1];payload=args.payload.resolve();output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    if not(payload/'EqlTracker.Linux').is_file():raise SystemExit('Expected a self-contained linux-x64 payload')
    assert_tree(payload)
    props={n.tag:n.text for n in ET.parse(root/'Version.props').getroot().find('PropertyGroup')};version=props['VersionPrefix']+'-'+props['VersionSuffix']
    name='EQL-Catalyst-'+version+'-linux-x64-preview';archive=output/(name+'.tar.gz')
    with tarfile.open(archive,'w:gz') as tar:
        for p in sorted(payload.rglob('*')):
            if p.is_symlink():raise ValueError('Symlinks are not accepted in a staged payload')
            if p.is_file():tar.add(p,arcname=name+'/'+p.relative_to(payload).as_posix(),recursive=False)
    with tarfile.open(archive) as tar:
        assert_names(tar.getnames());assert all(not m.name.startswith('/') and '..' not in Path(m.name).parts for m in tar.getmembers())
    packages=[archive]
    if shutil.which('dpkg-deb'):
        with tempfile.TemporaryDirectory(prefix='catalyst-deb-') as folder:
            stage=Path(folder);app=stage/'usr/lib/eql-catalyst';shutil.copytree(payload,app)
            (stage/'DEBIAN').mkdir();(stage/'DEBIAN/control').write_text(f'Package: eql-catalyst-preview\nVersion: {props["VersionPrefix"]}~{props["VersionSuffix"]}\nArchitecture: amd64\nMaintainer: EQL Catalyst contributors\nDepends: libc6, libgcc-s1, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1, libicu74, libssl3t64, zlib1g\nDescription: EQL Catalyst native desktop preview, not release-qualified\n')
            launcher=stage/'usr/bin/eql-catalyst';launcher.parent.mkdir(parents=True);launcher.write_text('#!/bin/sh\nexec /usr/lib/eql-catalyst/EqlTracker.Linux "$@"\n');launcher.chmod(0o755)
            desktop=stage/'usr/share/applications/eql-catalyst.desktop';desktop.parent.mkdir(parents=True);desktop.write_text('[Desktop Entry]\nType=Application\nName=EQL Catalyst Preview\nExec=eql-catalyst\nTerminal=false\nCategories=Utility;\n')
            deb=output/(name+'.deb');subprocess.run(['dpkg-deb','--root-owner-group','--build',str(stage),str(deb)],check=True);packages.append(deb)
    hashes={p.name:hashlib.file_digest(p.open('rb'),'sha256').hexdigest() for p in packages}
    (output/'SHA256SUMS.txt').write_text(''.join(v+'  '+k+'\n' for k,v in hashes.items()))
    (output/'linux-package-status.json').write_text(json.dumps(dict(Version=version,Preview=True,ReleaseQualified=False,Packages=hashes,PhysicalSteamDeckAcceptance=False),indent=2))
    print(json.dumps(dict(Packages=list(hashes),Published=False,ReleaseQualified=False)))
if __name__=='__main__':main()
