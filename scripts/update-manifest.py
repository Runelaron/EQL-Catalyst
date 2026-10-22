"""Create update metadata for already validated assets. Signing is a separate local step."""
import argparse,hashlib,json,re
from pathlib import Path
import xml.etree.ElementTree as ET

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--assets',type=Path,required=True);parser.add_argument('--commit',required=True);args=parser.parse_args()
    props={n.tag:n.text for n in ET.parse(Path(__file__).resolve().parents[1]/'Version.props').getroot().find('PropertyGroup')}
    version=props['VersionPrefix']+'-'+props['VersionSuffix']
    if not re.fullmatch('[0-9a-f]{40}',args.commit):raise SystemExit('Expected a full source commit SHA')
    packages=[]
    for kind,suffix in [('Installer','-win-x64-setup.exe'),('Portable','-win-x64.zip')]:
        path=args.assets/('EQL-Catalyst-'+version+suffix)
        packages.append({'Kind':kind,'Name':path.name,'Bytes':path.stat().st_size,'Sha256':hashlib.file_digest(path.open('rb'),'sha256').hexdigest()})
    data={'Schema':1,'Product':'EQL Catalyst','Repository':'Runelaron/EQL-Catalyst','Version':version,'Tag':'v'+version,'Commit':args.commit,'Architecture':'win-x64','MinimumUpdaterVersion':'1.4.0-beta.1','Packages':packages}
    (args.assets/'update-manifest.json').write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
    print('Created update-manifest.json for '+version)
if __name__=='__main__':main()
