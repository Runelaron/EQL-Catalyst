import io,tarfile,tempfile,unittest,hashlib,json,subprocess,sys
from pathlib import Path
from archive_contents import archive_entries
from package_policy import assert_archive

def tar_bytes(name,data,pax=None):
    out=io.BytesIO()
    with tarfile.open(fileobj=out,mode='w:gz') as archive:
        entry=tarfile.TarInfo(name);entry.size=len(data);entry.pax_headers=pax or {};archive.addfile(entry,io.BytesIO(data))
    return out.getvalue()

def deb_bytes(data):
    out=b'!<arch>\n'
    for name,body in [('debian-binary',b'2.0\n'),('control.tar.gz',tar_bytes('control',b'Package: example\n')),('data.tar.gz',data)]:
        header=f'{name+"/":<16}{0:<12}{0:<6}{0:<6}{"100644":<8}{len(body):<10}`\n'.encode();out+=header+body+(b'\n' if len(body)%2 else b'')
    return out

class NativeArchiveTests(unittest.TestCase):
    def audit(self,path,records=()):
        review=path.parent/'review.json';review.write_text(json.dumps(records))
        run=subprocess.run([sys.executable,str(Path(__file__).with_name('privacy-audit.py')),str(path),'--exceptions',str(review)],capture_output=True,text=True)
        return run.returncode,json.loads(run.stdout)
    def test_tar_and_deb_reject_private_contents(self):
        with tempfile.TemporaryDirectory() as folder:
            for extension,body in [('.tar.gz',tar_bytes('profiles/example.json',b'{}')),('.deb',deb_bytes(tar_bytes('profiles/example.json',b'{}')))]:
                path=Path(folder)/('package'+extension);path.write_bytes(body)
                with self.assertRaises(ValueError):assert_archive(path)
    def test_traversal_and_links_rejected_without_extraction(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'package.tar.gz';path.write_bytes(tar_bytes('../escape',b'data'))
            with self.assertRaises(ValueError):list(archive_entries(path))
            with tarfile.open(path,'w:gz') as archive:
                info=tarfile.TarInfo('link');info.type=tarfile.SYMTYPE;info.linkname='/tmp/example';archive.addfile(info)
            with self.assertRaises(ValueError):assert_archive(path)
    def test_tar_metadata_scanned(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'package.tar.gz';path.write_bytes(tar_bytes('safe',b'safe',{'comment':'.'.join(['10','12','3','4'])}))
            code,report=self.audit(path);self.assertEqual(code,1);self.assertTrue(any('#metadata' in f['File'] for f in report['UnresolvedFindings']))
    def test_linux_review_bound_to_complete_inner_path_and_bytes(self):
        with tempfile.TemporaryDirectory() as folder:
            data='.'.join(['10','12','3','4']).encode();record=dict(File='library.dll',Rule='private-ip-literal',Sha256=hashlib.sha256(data).hexdigest(),Reason='Synthetic constant')
            path=Path(folder)/'package.tar.gz'
            for name,content,expected in [('EQL-Catalyst-1.8.0-beta.1-linux-x64-preview/library.dll',data,0),('other/library.dll',data,1),('EQL-Catalyst-1.8.0-beta.1-linux-x64-preview/library.dll',data+b'x',1)]:
                path.write_bytes(tar_bytes(name,content));code,_=self.audit(path,[record]);self.assertEqual(code,expected)
            path=Path(folder)/'package.deb';path.write_bytes(deb_bytes(tar_bytes('usr/lib/eql-catalyst/library.dll',data)))
            record['File']='usr/lib/eql-catalyst/library.dll';code,report=self.audit(path,[record]);self.assertEqual(code,0);self.assertEqual(len(report['ReviewedFindings']),1)
            record['File']='library.dll';code,_=self.audit(path,[record]);self.assertEqual(code,1)

if __name__=='__main__':unittest.main()
