"""Build local unsigned release artifacts from the maintained source tree; never publishes."""
from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED
import argparse,hashlib,json,os,re,shutil,subprocess,urllib.request,uuid
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
DIST=ROOT/'dist'; TOOLS=ROOT/'.tools/installer'
URL='https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
PIN='0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
HIDDEN=getattr(subprocess,'CREATE_NO_WINDOW',0)
def run(args,**kw):
    if kw.get('capture_output'):return subprocess.run([str(a) for a in args],cwd=ROOT,check=True,creationflags=HIDDEN,**kw)
    result=subprocess.run([str(a) for a in args],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace',creationflags=HIDDEN,**kw)
    print(result.stdout+result.stderr,flush=True);result.check_returncode();return result
def bootstrap():
    TOOLS.mkdir(parents=True,exist_ok=True); setup=TOOLS/'inno-setup.exe'
    if not setup.exists():
        with urllib.request.urlopen(URL,timeout=120) as response:setup.write_bytes(response.read())
    if sha(setup)!=PIN:raise RuntimeError('Inno Setup checksum mismatch')
    quoted=str(setup).replace("'","''")
    shell=shutil.which('pwsh') or shutil.which('powershell')
    if not shell:raise RuntimeError('PowerShell is required for publisher verification')
    run([shell,'-NoProfile','-Command',f"$ErrorActionPreference='Stop'; $s=Get-AuthenticodeSignature -LiteralPath '{quoted}'; $s | Select-Object Status,StatusMessage,@{{n='Signer';e={{$_.SignerCertificate.Subject}}}},@{{n='Thumbprint';e={{$_.SignerCertificate.Thumbprint}}}} | ConvertTo-Json -Compress; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'Pyrsys B.V.'){{exit 1}}"])
    run([setup,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS','/TASKS=',f'/DIR={TOOLS/"compiler"}'])
    return TOOLS/'compiler/ISCC.exe'
def source_files():
    for p in sorted(ROOT.rglob('*')):
        rel=p.relative_to(ROOT)
        if p.is_file() and not any(part in {'.git','.tools','bin','obj','dist','artifacts','__pycache__'} for part in rel.parts) and not p.name.endswith(('.db','.db-wal','.db-shm','.log','.user')):yield p
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--dotnet',default=shutil.which('dotnet'));parser.add_argument('--compiler');parser.add_argument('--bootstrap-compiler',action='store_true');parser.add_argument('--skip-tests',action='store_true');parser.add_argument('--package-only',action='store_true');parser.add_argument('--payload-only',action='store_true');args=parser.parse_args()
    props={e.tag:e.text for e in ET.parse(ROOT/'Version.props').getroot().find('PropertyGroup')};version=props['VersionPrefix']+'-'+props['VersionSuffix'];stage=re.fullmatch(r'beta\.(\d+)',props['VersionSuffix'])
    if not stage:raise RuntimeError('Expected beta.N version')
    DIST.mkdir(exist_ok=True);payload=DIST/f'EQL-Catalyst-{version}-win-x64'
    if not args.package_only:
        if not args.dotnet:raise RuntimeError('Install the SDK from global.json, or pass --dotnet')
        if not args.skip_tests:run([args.dotnet,'run','--project','tests/EqlTracker.Tests','-c','Release','--no-launch-profile'])
        temp=DIST/('.publish-'+uuid.uuid4().hex)
        run([args.dotnet,'publish','src/EqlTracker.App','-c','Release','-r','win-x64','--self-contained','true','-o',temp,'--nologo','-p:RestorePackagesWithLockFile=true'])
        run([args.dotnet,'publish','src/EqlTracker.Updater','-c','Release','-r','win-x64','--self-contained','true','-o',temp,'--nologo','-p:RestorePackagesWithLockFile=true'])
        if payload.exists():
            if not payload.resolve().is_relative_to(DIST.resolve()):raise RuntimeError('Unsafe payload path')
            shutil.rmtree(payload)
        temp.rename(payload)
        for p in payload.rglob('*.pdb'):p.unlink()
        for name in ['LICENSE','THIRD-PARTY-NOTICES.md','ASSET-NOTICES.md']:shutil.copy2(ROOT/name,payload/name)
        shutil.copy2(ROOT/'docs/installation.md',payload/'README.md')
        shutil.copy2(ROOT/'docs/role-ratings.md',payload/'ROLE-RATINGS.md')
        shutil.copy2(ROOT/'docs/pet-comparison.md',payload/'PET-COMPARISON.md')
        shutil.copy2(ROOT/'docs/application-updates.md',payload/'APPLICATION-UPDATES.md')
        shutil.copy2(ROOT/'docs/progress-rewards.md',payload/'PROGRESS-REWARDS.md')
        (payload/'licenses').mkdir(exist_ok=True)
        sdks=run([args.dotnet,'--list-sdks'],capture_output=True,text=True).stdout
        sdkroot=Path(re.findall(r'\[([^\]]+)\]',sdks)[-1]).parent
        for name in ['LICENSE.txt','ThirdPartyNotices.txt']:
            if not(sdkroot/name).exists():raise RuntimeError('Runtime notice missing: '+name)
            shutil.copy2(sdkroot/name,payload/'licenses'/('dotnet-'+name))
        nuget=Path(os.environ.get('NUGET_PACKAGES',Path.home()/'.nuget/packages'))
        # Only licenses for the exact package versions present in the output dependency graph.
        deps=json.loads((payload/'EQLTracker.deps.json').read_text(encoding='utf-8'))
        for identity,library in deps['libraries'].items():
            if library.get('type')!='package':continue
            package,version_number=identity.rsplit('/',1);folder=nuget/package.lower()/version_number
            for p in folder.rglob('*'):
                if p.is_file() and ('license' in p.name.lower() or p.suffix=='.nuspec'):
                    target=payload/'licenses'/(package+'-'+version_number+'-'+p.name)
                    shutil.copy2(p,target)
        manifest={p.relative_to(payload).as_posix():sha(p) for p in sorted(payload.rglob('*')) if p.is_file()}
        (payload/'release-manifest.json').write_text(json.dumps({'Product':'EQL Catalyst','Version':props['VersionPrefix']+'-'+props['VersionSuffix'],'WindowsVersion':props['FileVersion'],'Signed':False,'Files':manifest},indent=2),encoding='utf-8')
        if args.payload_only:return
        compiler=Path(args.compiler) if args.compiler else bootstrap() if args.bootstrap_compiler else TOOLS/'compiler/ISCC.exe'
        if not compiler.exists() or '7.1.0' not in run([compiler,'--version'],capture_output=True,text=True).stdout:raise RuntimeError('Verified Inno Setup 7.1.0 required')
        run([compiler,f'--define=Payload={payload}',f'--define=ReleaseVersion={props["VersionPrefix"]}-{props["VersionSuffix"]}',f'--define=ReleaseLabel={props["ReleaseLabel"]}',f'--define=WindowsVersion={props["FileVersion"]}',f'--define=ReleaseOrder={props["VersionPrefix"]}.{stage[1]}','installer/EqlCatalyst.iss'])
    version=props['VersionPrefix']+'-'+props['VersionSuffix']
    manifest=json.loads((payload/'release-manifest.json').read_text(encoding='utf-8'))
    for name,digest in manifest['Files'].items():
        if sha(payload/name)!=digest:raise RuntimeError('Payload changed: '+name)
    portable=DIST/f'EQL-Catalyst-{version}-win-x64.zip';source=DIST/f'EQL-Catalyst-{version}-source.zip'
    for path,files,base in [(portable,sorted(payload.rglob('*')),DIST),(source,list(source_files()),ROOT)]:
        with ZipFile(path,'w',ZIP_DEFLATED,compresslevel=6) as archive:
            for p in files:
                if p.is_file():archive.write(p,p.relative_to(base).as_posix())
        with ZipFile(path) as archive:
            if archive.testzip():raise RuntimeError('ZIP CRC failure')
    artifacts=[DIST/f'EQL-Catalyst-{version}-win-x64-setup.exe',portable,source]
    checks={p.name:{'Bytes':p.stat().st_size,'Sha256':sha(p)} for p in artifacts}
    (DIST/'SHA256SUMS.txt').write_text(''.join(v['Sha256']+'  '+name+'\n' for name,v in checks.items()),encoding='ascii')
    (DIST/'packages.json').write_text(json.dumps({'Version':version,'Unsigned':True,'Packages':checks},indent=2),encoding='utf-8')
    print(json.dumps({'Version':version,'Packages':checks}))
if __name__=='__main__':main()
