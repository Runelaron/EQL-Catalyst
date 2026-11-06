"""Package an already built Linux preview. Does not publish or modify the host."""
from pathlib import Path
import argparse, hashlib, json, os, shutil, subprocess, tarfile, tempfile
import xml.etree.ElementTree as ET
from package_policy import assert_tree, assert_archive

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--payload',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    root=Path(__file__).resolve().parents[1];payload=args.payload.resolve();output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    if not(payload/'EqlTracker.Linux').is_file():raise SystemExit('Expected a self-contained linux-x64 payload')
    # Include the exact application's, font's, runtime's and dependency notices.
    for name in ['LICENSE','THIRD-PARTY-NOTICES.md','ASSET-NOTICES.md']:
        shutil.copy2(root/name,payload/name)
    shutil.copy2(root/'docs/linux-desktop.md',payload/'README.md')
    licenses=payload/'licenses';licenses.mkdir(exist_ok=True)
    sdkroot=Path(os.environ['DOTNET_ROOT'])
    for name in ['LICENSE.txt','ThirdPartyNotices.txt']:
        if not(sdkroot/name).is_file():raise ValueError('Missing runtime notice: '+name)
        shutil.copy2(sdkroot/name,licenses/('dotnet-'+name))
    deps=json.loads((payload/'EqlTracker.Linux.deps.json').read_text())
    nuget=Path(os.environ.get('NUGET_PACKAGES',Path.home()/'.nuget/packages'))
    for identity,library in deps['libraries'].items():
        if library.get('type')!='package':continue
        package,version=identity.rsplit('/',1)
        for file in (nuget/package.lower()/version).rglob('*'):
            if file.is_file() and ('license' in file.name.lower() or file.suffix=='.nuspec'):
                shutil.copy2(file,licenses/(package+'-'+version+'-'+file.name))
    assert_tree(payload)
    props={n.tag:n.text for n in ET.parse(root/'Version.props').getroot().find('PropertyGroup')};version=props['VersionPrefix']+'-'+props['VersionSuffix']
    manifest={p.relative_to(payload).as_posix():hashlib.file_digest(p.open('rb'),'sha256').hexdigest() for p in sorted(payload.rglob('*')) if p.is_file() and p.name!='release-manifest.json'}
    commit=subprocess.run(['git','rev-parse','HEAD'],cwd=root,capture_output=True,text=True,check=True).stdout.strip()
    (payload/'release-manifest.json').write_text(json.dumps(dict(Product='EQL Catalyst',Version=version,Runtime='linux-x64',SourceCommit=commit,Preview=True,ReleaseQualified=False,Files=manifest),indent=2))
    name='EQL-Catalyst-'+version+'-linux-x64-preview';archive=output/(name+'.tar.gz')
    with tarfile.open(archive,'w:gz') as tar:
        for p in sorted(payload.rglob('*')):
            if p.is_symlink():raise ValueError('Symlinks are not accepted in a staged payload')
            if p.is_file():
                info=tar.gettarinfo(str(p),arcname=name+'/'+p.relative_to(payload).as_posix())
                info.uid=info.gid=0;info.uname=info.gname='root'
                with p.open('rb') as stream:tar.addfile(info,stream)
    assert_archive(archive)
    packages=[archive]
    if shutil.which('dpkg-deb'):
        with tempfile.TemporaryDirectory(prefix='catalyst-deb-') as folder:
            stage=Path(folder);app=stage/'usr/lib/eql-catalyst';shutil.copytree(payload,app)
            (stage/'DEBIAN').mkdir();(stage/'DEBIAN/control').write_text(f'Package: eql-catalyst-preview\nVersion: {props["VersionPrefix"]}~{props["VersionSuffix"]}\nArchitecture: amd64\nMaintainer: EQL Catalyst contributors\nDepends: libc6, libgcc-s1, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1, libicu74, libssl3t64, zlib1g\nDescription: EQL Catalyst native desktop preview, not release-qualified\n')
            launcher=stage/'usr/bin/eql-catalyst';launcher.parent.mkdir(parents=True);launcher.write_text('#!/bin/sh\nexec /usr/lib/eql-catalyst/EqlTracker.Linux "$@"\n');launcher.chmod(0o755)
            desktop=stage/'usr/share/applications/eql-catalyst.desktop';desktop.parent.mkdir(parents=True);desktop.write_text('[Desktop Entry]\nType=Application\nName=EQL Catalyst Preview\nExec=eql-catalyst\nTerminal=false\nCategories=Utility;\n')
            deb=output/(name+'.deb');subprocess.run(['dpkg-deb','-Zgzip','--root-owner-group','--build',str(stage),str(deb)],check=True);assert_archive(deb);packages.append(deb)
    hashes={p.name:hashlib.file_digest(p.open('rb'),'sha256').hexdigest() for p in packages}
    (output/'SHA256SUMS.txt').write_text(''.join(v+'  '+k+'\n' for k,v in hashes.items()))
    (output/'linux-package-status.json').write_text(json.dumps(dict(Version=version,Preview=True,ReleaseQualified=False,Packages=hashes,PhysicalSteamDeckAcceptance=False),indent=2))
    print(json.dumps(dict(Packages=list(hashes),Published=False,ReleaseQualified=False)))
if __name__=='__main__':main()
