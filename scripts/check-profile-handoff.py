"""Exercise a whole-application character handoff in an isolated, offline data root only."""
from pathlib import Path
import argparse,json,sqlite3,subprocess,time,uuid

def normalized(value):
    if isinstance(value,dict):return {k.lower():normalized(v) for k,v in value.items()}
    if isinstance(value,list):return [normalized(v) for v in value]
    return value

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--dotnet',required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    repo=Path(__file__).resolve().parents[1];app=repo/'src/EqlTracker.App/bin/Release/net10.0-windows/EQLTracker.dll'
    args.output.mkdir(parents=True,exist_ok=True);token=uuid.uuid4().hex[:10]
    root=args.output/('eql-switch-review-'+token);first='ReviewA'+token;second='ReviewB'+token
    root.mkdir();base=[args.dotnet,str(app),'--data-root',str(root)]
    checks=[]
    def check(condition,name):
        if not condition:raise AssertionError(name)
        checks.append(name)
    def run(tail):return subprocess.run(base+tail,cwd=repo,capture_output=True,timeout=60,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0)).returncode
    check(run(['--character','ExampleHero','--server',first,'--launch-audit'])==0,'First offline character opens')
    file_a=root/('ExampleHero_'+first)/'tracker.db'
    with sqlite3.connect(file_a) as db:
        profile=json.loads(db.execute("select json from state where key='profile'").fetchone()[0]);profile[next(k for k in profile if k.lower()=='theme')]='shadowman'
        db.execute("update state set json=? where key='profile'",(json.dumps(profile),))
        inventory=json.loads(db.execute("select json from state where key='inventory'").fetchone()[0])
        baseline_inventory=json.dumps(inventory,sort_keys=True)
    check(run(['--character','ExampleHero','--server',first,'--character-switch-audit',second])==0,'Parent saves and launches selected server through the real handoff command')
    audit=root/('ExampleHero_'+second)/'launch-audit.json';deadline=time.monotonic()+50
    while not audit.exists() and time.monotonic()<deadline:time.sleep(.2)
    check(audit.exists(),'Child waits for parent exit then opens its separate database')
    observed=normalized(json.loads(audit.read_text()));check(observed['server']==second and observed['character']=='ExampleHero','Same character name on another server remains a different identity')
    check(not observed['nativesync']['periodic'] and not observed['nativesync']['login'] and not observed['nativesync']['verified'],'Switching does not opt into captures')
    check(not observed['gamefolder'] and observed['level']==0 and not observed['currentclasses'],'New offline character facts stay unknown')
    with sqlite3.connect(file_a) as db:
        saved=json.loads(db.execute("select json from state where key='profile'").fetchone()[0]);stock=json.loads(db.execute("select json from state where key='inventory'").fetchone()[0])
    saved=normalized(saved)
    check(saved['server']==first and saved['theme']=='shadowman','Previous identity and appearance remain in the original profile')
    check(json.dumps(stock,sort_keys=True)==baseline_inventory,'Previous inventory ownership evidence is unchanged')
    launcher=normalized(json.loads((root/'launcher.json').read_text()));check(launcher['lastprofile']['server']==second,'Successful handoff remembers the selected character/server')
    check(len(launcher['knownprofiles'])==2,'Both server identities remain in the character registry')
    check(run(['--launch-audit'])==0,'Restart opens the last successfully selected character')
    (args.output/'profile-handoff-checks.json').write_text(json.dumps({'Passed':len(checks),'Checks':checks,'Offline':True,'DedicatedDataRoot':root.name,'NoNativeInput':True},indent=2)+'\n')
    print(json.dumps({'Passed':len(checks),'ProfileHandoff':'Passed','Isolated':True}))

if __name__=='__main__':main()
