"""Gate self-contained payload declarations against the reviewed security floor."""
from pathlib import Path
import json,re

def verify_runtime(payload, app, expected):
    def version(value):
        if not re.fullmatch(r'\d+\.\d+\.\d+',value):raise ValueError('Expected a stable runtime version')
        return tuple(map(int,value.split('.')))
    if version(expected)<(10,0,14):raise ValueError('Runtime predates the CVE-2026-70197 security fix')
    config=json.loads((Path(payload)/(app+'.runtimeconfig.json')).read_text())
    frameworks=config['runtimeOptions'].get('includedFrameworks',[])
    names=[f['name'] for f in frameworks]
    if not frameworks or 'Microsoft.NETCore.App' not in names or len(names)!=len(set(names)) or any(f['version']!=expected for f in frameworks):
        raise ValueError('Self-contained frameworks do not match the reviewed runtime version')
    return frameworks
