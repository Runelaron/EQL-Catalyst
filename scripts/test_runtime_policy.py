import json,tempfile,unittest
from pathlib import Path
from runtime_policy import verify_runtime

class RuntimePolicyTests(unittest.TestCase):
    def test_both_platforms_reject_old_mixed_and_framework_dependent_payloads(self):
        with tempfile.TemporaryDirectory() as folder:
            for app in ['EQLTracker','EqlTracker.Linux']:
                path=Path(folder)/(app+'.runtimeconfig.json')
                for frameworks in [[],[dict(name='Microsoft.NETCore.App',version='10.0.13')],[dict(name='Microsoft.NETCore.App',version='10.0.14'),dict(name='Microsoft.AspNetCore.App',version='10.0.13')],[dict(name='Microsoft.NETCore.App',version='10.0.14')]*2]:
                    path.write_text(json.dumps(dict(runtimeOptions=dict(includedFrameworks=frameworks))))
                    with self.assertRaises(ValueError):verify_runtime(folder,app,'10.0.14')
                path.write_text(json.dumps(dict(runtimeOptions=dict(includedFrameworks=[dict(name='Microsoft.NETCore.App',version='10.0.14')]))))
                self.assertEqual(len(verify_runtime(folder,app,'10.0.14')),1)
                with self.assertRaises(ValueError):verify_runtime(folder,app,'10.0.13')

if __name__=='__main__':unittest.main()
