"""Exercise signed updates on disposable copies; installed cases require a hosted runner."""
from pathlib import Path
import argparse, base64, hashlib, json, os, shutil, subprocess, time, urllib.request, zipfile

ROOT=Path(__file__).resolve().parents[1]
HIDDEN=getattr(subprocess,'CREATE_NO_WINDOW',0)
sha=lambda p:hashlib.file_digest(p.open('rb'),'sha256').hexdigest()
def run(args,cwd):
    return subprocess.run([str(a) for a in args],cwd=cwd,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=HIDDEN,timeout=180)
def extract(path,destination):
    with zipfile.ZipFile(path) as z:
        for n in z.namelist():
            target=(destination/n).resolve()
            if not target.is_relative_to(destination.resolve()) or ':' in n:raise ValueError('Unsafe archive path')
        z.extractall(destination)
    return next(p for p in destination.iterdir() if p.is_dir())
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--assets',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--portable-only',action='store_true');args=parser.parse_args()
    if not args.portable_only and (os.environ.get('GITHUB_ACTIONS')!='true' or not os.environ.get('RUNNER_TEMP')):raise SystemExit('Installed tests require a disposable GitHub-hosted runner')
    assets=args.assets.resolve();out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
    manifest=json.loads((assets/'update-manifest.json').read_text());version=manifest['Version'];results=[]
    for p in manifest['Packages']:
        if sha(assets/p['Name'])!=p['Sha256']:raise ValueError('Candidate package digest mismatch')
    baselines=json.loads((ROOT/'tests/fixtures/update-baselines.json').read_text())
    downloads=out/'downloads';downloads.mkdir(exist_ok=True)
    for baseline in baselines:
        for package in baseline['Packages']:
            path=downloads/package['Name']
            if not path.exists():
                print('Download '+package['Name'],flush=True)
                with urllib.request.urlopen(package['Url'],timeout=120) as response,path.open('wb') as output:shutil.copyfileobj(response,output)
            if sha(path)!=package['Sha256']:raise ValueError('Baseline package digest mismatch')
        for scope in (['Portable'] if args.portable_only else ['Portable','CurrentUser','AllUsers']):
            print('Update '+baseline['Version']+' / '+scope,flush=True)
            case=out/(baseline['Version']+'-'+scope)
            if case.exists():raise ValueError('Use a new update-test output directory')
            data=case/'profiles';job=data/'updates/test-job';job.mkdir(parents=True)
            app=case/'Application';old={p['Kind']:downloads/p['Name'] for p in baseline['Packages']}
            if scope=='Portable':shutil.copytree(extract(old['Portable'],case/'baseline'),app)
            else:run([old['Installer'],'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/'+scope.upper(),'/DIR='+str(app)],case)
            expected_old=sha(app/'EQLTracker.exe')
            for name in ['update-manifest.json','update-manifest.sig']+[p['Name'] for p in manifest['Packages']]:shutil.copy2(assets/name,job/name)
            package={p['Kind']:job/p['Name'] for p in manifest['Packages']};payload=extract(package['Portable'],job/'extracted')
            sentinel=data/'preservation.txt';sentinel.write_text('Synthetic profile sentinel; preserve exactly.',encoding='utf-8')
            actors=['UpdateHero','UpdateAlt'];processes=[]
            for actor in actors:processes.append(subprocess.Popen([str(app/'EQLTracker.exe'),'--character',actor,'--server','example','--data-root',str(data),'--game-folder','','--offline'],cwd=app,creationflags=HIDDEN))
            time.sleep(5)
            if not all(p.poll() is None for p in processes):raise ValueError('Baseline did not remain running')
            intent={'AppDirectory':str(app),'DataRoot':str(data),'PayloadDirectory':str(payload),'Installer':str(package['Installer']),'Scope':scope,'Character':actors[0],'Server':'example','GameFolder':'','Version':version,'JobDirectory':str(job)}
            (job/'apply.json').write_text(json.dumps(intent),encoding='utf-8')
            # Invalid signature is rejected before preparing or closing any baseline instance.
            signature=(job/'update-manifest.sig').read_text();invalid=bytearray(base64.b64decode(signature));invalid[0]^=1
            (job/'update-manifest.sig').write_text(base64.b64encode(invalid).decode(),encoding='ascii')
            run([payload/'EQLUpdater.exe','--apply',job/'apply.json','--offscreen'],payload)
            if not (job/'helper-error.json').exists() or not all(p.poll() is None for p in processes) or sha(app/'EQLTracker.exe')!=expected_old:raise ValueError('Invalid metadata changed the baseline')
            (job/'rejected-metadata.json').write_bytes((job/'helper-error.json').read_bytes());(job/'helper-error.json').unlink();(job/'update-manifest.sig').write_text(signature,encoding='ascii')
            run([payload/'EQLUpdater.exe','--apply',job/'apply.json','--offscreen'],payload)
            if (job/'helper-error.json').exists():raise RuntimeError((job/'helper-error.json').read_text())
            if not (job/'completed.json').exists() or not all(p.poll() is not None for p in processes):raise ValueError('Update did not finish gracefully')
            files=json.loads((app/'release-manifest.json').read_text())['Files']
            for name,digest in files.items():
                if sha(app/name)!=digest:raise ValueError('Updated payload mismatch: '+name)
            receipts=list((data/'updates/receipts').glob('*.json'))
            if len(receipts)!=2 or not all(json.loads(p.read_text())['Version'].split('+')[0]==version for p in receipts):raise ValueError('Missing startup receipts')
            if sentinel.read_text()!='Synthetic profile sentinel; preserve exactly.' or len(list((job/'recovery').glob('profiles-*')))!=1:raise ValueError('Recovery or profile preservation failed')
            if scope!='Portable' and not (job/'recovery/application/EQLTracker.exe').exists():raise ValueError('Installed application recovery missing')
            for actor in actors:run([app/'EQLTracker.exe','--character',actor,'--server','example','--data-root',data,'--shutdown'],app)
            time.sleep(1)
            if scope!='Portable':run([app/'unins000.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'],case)
            if sentinel.read_text()!='Synthetic profile sentinel; preserve exactly.':raise ValueError('Uninstall modified profile')
            results.append({'FromVersion':baseline['Version'],'Version':version,'Scope':scope,'Passed':True,'Profiles':2,'PayloadFiles':len(files),'SignatureRejectionPreservesRunningCopy':True,'RecoveryCreated':True,'StartupReceipts':2,'ProfileSentinelPreserved':True,'ForcedTermination':False})
    (out/'upgrade-validation.json').write_text(json.dumps({'Passed':True,'Commit':manifest['Commit'],'Cases':results,'Scope':'Disposable copies; actual signed helper, graceful multi-profile restart and backups. Dirty-work cancellation is covered separately by native workspace tests.'},indent=2),encoding='utf-8')
    print(json.dumps({'Passed':True,'Cases':len(results)}))
if __name__=='__main__':main()
