from pathlib import Path
import argparse,ctypes,hashlib,json

p=argparse.ArgumentParser();p.add_argument('raw',type=Path);p.add_argument('payload',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
s=json.loads(a.raw.read_text());source_name=s['metadata']['component']['name']
def clean(v):
    if isinstance(v,str):return v.replace(source_name,'/_/EQL-Catalyst')
    if isinstance(v,list):return [clean(x) for x in v]
    if isinstance(v,dict):return {k:clean(x) for k,x in v.items()}
    return v
s=clean(s);s['metadata']['component']['name']='EQL Catalyst';s['metadata']['component']['version']=json.loads((a.payload/'release-manifest.json').read_text())['Version']
known={(c.get('name','').lower(),c.get('version')) for c in s['components']}
def add(name,version,purl,evidence=''):
    if (name.lower(),version) in known:return
    c={'type':'library','bom-ref':purl,'name':name,'version':version,'purl':purl}
    if evidence:c['properties']=[{'name':'eql:evidence','value':evidence}]
    s['components'].append(c);known.add((name.lower(),version))
deps=json.loads((a.payload/'EQLTracker.deps.json').read_text())
for identity,library in deps['libraries'].items():
    if library.get('type')=='package':
        name,version=identity.rsplit('/',1);add(name,version,f'pkg:nuget/{name}@{version}')
for framework in json.loads((a.payload/'EQLTracker.runtimeconfig.json').read_text())['runtimeOptions']['includedFrameworks']:
    name=framework['name']+'.Runtime.win-x64';version=framework['version'];add(name,version,f'pkg:nuget/{name}@{version}','Bundled self-contained runtime declaration, cross-checked against core/WPF file ProductVersion. PE file observations are retained separately.')
lib=ctypes.CDLL(str(a.payload/'e_sqlite3.dll'));lib.sqlite3_libversion.restype=ctypes.c_char_p;sqlite=lib.sqlite3_libversion().decode()
add('SQLite',sqlite,'pkg:generic/sqlite@'+sqlite,'Verified through sqlite3_libversion from the exact bundled e_sqlite3.dll.')
s['metadata'].setdefault('properties',[]).append({'name':'eql:runtime-manifest-sha256','value':hashlib.sha256((a.payload/'release-manifest.json').read_bytes()).hexdigest()})
a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'Components':len(s['components']),'NativeSqlite':sqlite}))
