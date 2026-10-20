"""Inspect release inputs without printing matching private values. Nonzero means review required."""
from pathlib import Path
from zipfile import ZipFile
import argparse,hashlib,ipaddress,json,re,struct

PRIVATE=[re.compile(r'(?i)[a-z]:[/\\]+users[/\\]+(?!public\b|default\b)[^/\\\s"<>]+'),re.compile(r'/(?:home|Users)/[A-Za-z][A-Za-z0-9_.-]+/'),re.compile(r'(?i)codex-clipboard-[a-f0-9-]+'),re.compile(r'(?i)(?:gh[pousr]_[a-z0-9]{30,}|github_pat_[a-z0-9_]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')]
SKIP={'.git','.tools','bin','obj','dist','artifacts','__pycache__'}
def inspect(name,data,roots,findings,software_versions):
    texts=[data.decode('utf-8','ignore'),data.decode('utf-16-le','ignore')]
    for text in texts:
        normalized=text.replace('\\\\','/').replace('\\','/').casefold()
        for root in roots:
            if root and root in normalized:findings.add((name,'local-build-root'))
        # Third-party runtime build paths may appear in Microsoft binaries; all such matches are reviewed.
        for pattern in PRIVATE:
            if pattern.search(text):findings.add((name,'private-path-or-secret-pattern'))
        for value in re.findall(r'(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])',text):
            try:
                address=ipaddress.ip_address(value)
                if address.is_private and not address.is_loopback and not address.is_unspecified and not value.startswith('0.') and value not in software_versions:findings.add((name,'private-ip-literal'))
            except ValueError:pass
    if data.startswith(b'\x89PNG\r\n\x1a\n'):
        offset=8
        while offset+12<=len(data):
            size=struct.unpack('>I',data[offset:offset+4])[0];kind=data[offset+4:offset+8]
            if kind in [b'tEXt',b'zTXt',b'iTXt',b'eXIf']:findings.add((name,'image-metadata'))
            offset+=12+size
def main():
    parser=argparse.ArgumentParser();parser.add_argument('path',type=Path);parser.add_argument('--private-root',action='append',default=[]);parser.add_argument('--software-version',action='append',default=[],help='Exact reviewed dotted software-version literal, not a network address');parser.add_argument('--report',type=Path);args=parser.parse_args()
    roots=[s.replace('\\','/').casefold().rstrip('/') for s in args.private_root];findings=set();count=0
    paths=[args.path] if args.path.is_file() else [p for p in args.path.rglob('*') if p.is_file() and not any(x in SKIP for x in p.relative_to(args.path).parts)]
    for p in paths:
        name=p.name if args.path.is_file() else p.relative_to(args.path).as_posix();data=p.read_bytes();count+=1
        inspect(name,data,roots,findings,args.software_version)
        if p.suffix=='.zip':
            with ZipFile(p) as archive:
                for entry in archive.infolist():
                    if entry.is_dir():continue
                    if entry.filename.startswith(('/','\\')) or '..' in Path(entry.filename).parts:findings.add((name,'unsafe-archive-entry'))
                    inspect(name+'!'+entry.filename,archive.read(entry),roots,findings,args.software_version);count+=1
    result={'FilesInspected':count,'Passed':not findings,'Findings':[{'File':n,'Rule':r} for n,r in sorted(findings)],'ReviewedSoftwareVersionLiterals':args.software_version,'PrivateValuesPrinted':False}
    if args.report:args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result));raise SystemExit(1 if findings else 0)
if __name__=='__main__':main()
