import hashlib,json,subprocess,sys,tempfile,unittest
from pathlib import Path
from zipfile import ZipFile

class PrivacyReviewTests(unittest.TestCase):
    def run_audit(self,path,records=None):
        command=[sys.executable,str(Path(__file__).with_name('privacy-audit.py')),str(path)]
        if records is not None:
            review=path.parent/'reviews.json';review.write_text(json.dumps(records));command+=['--exceptions',str(review)]
        result=subprocess.run(command,capture_output=True,text=True)
        return result.returncode,json.loads(result.stdout)
    def test_strict_without_review(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'fixture.txt';p.write_text('.'.join(['10','12','3','4']))
            code,result=self.run_audit(p);self.assertEqual(code,1);self.assertFalse(result['Passed']);self.assertEqual(len(result['UnresolvedFindings']),1)
    def test_review_is_bound_to_file_rule_and_content(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'fixture.txt';p.write_text('.'.join(['10','12','3','4'])+'\n')
            record={'File':p.name,'Rule':'private-ip-literal','Sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'Reason':'Synthetic subnet validation fixture'}
            code,result=self.run_audit(p,[record]);self.assertEqual(code,0);self.assertEqual(len(result['Findings']),1);self.assertEqual(len(result['ReviewedFindings']),1)
            p.write_text('.'.join(['10','12','3','5'])+'\n');code,result=self.run_audit(p,[record]);self.assertEqual(code,1);self.assertEqual(len(result['ReviewedFindings']),0)
    def test_archive_entries_checked_against_exact_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            folder=Path(directory);data=('.'.join(['10','12','3','4'])+'\n').encode();p=folder/'source.zip'
            with ZipFile(p,'w') as archive:archive.writestr('tests/fixture.txt',data)
            code,result=self.run_audit(p,[{'File':'tests/fixture.txt','Rule':'private-ip-literal','Sha256':hashlib.sha256(data).hexdigest(),'Reason':'Synthetic fixture'}]);self.assertEqual(code,0);self.assertTrue(any('!' in x['File'] for x in result['ReviewedFindings']))
    def test_archive_metadata_is_not_hidden_by_compression(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'source.zip'
            with ZipFile(p,'w') as archive:archive.writestr('safe.txt','safe');archive.comment='.'.join(['10','12','3','4']).encode()
            code,result=self.run_audit(p);self.assertEqual(code,1);self.assertTrue(any('metadata' in x['File'] for x in result['UnresolvedFindings']))

if __name__=='__main__':unittest.main()
