import importlib.util,tempfile,unittest
from pathlib import Path
from zipfile import ZipFile
from package_policy import assert_archive,assert_names,assert_tree,forbidden

class PackagePolicyTests(unittest.TestCase):
    def test_entrypoints_and_shared_guide_rejected(self):
        for name in ['AGENTS.md','source/agents.MD','.tools/project-guidance/README.md','docs/project-guidance/a.md','payload/private-research/sheets.json']:
            with self.subTest(name=name):
                with self.assertRaises(ValueError):assert_names([name])
    def test_product_content_allowed(self):assert_names(['src/EqlTracker.App/OverviewWindow.cs','OVERVIEW.md','data/catalog.json','companion-web/index.html'])
    def test_personal_state_and_credentials_rejected(self):
        for name in ['profiles/hero.json','companion-private/server-id.txt','identity.dpapi','browser.pfx','local.crt','trusted.cer','identity.der','chain.p7b','pairing.p12','release.key','private.pem','android/debug.keystore','release.jks','tracker.db','capture.log','session.har']:
            with self.subTest(name=name):self.assertTrue(forbidden(name))
    def test_exact_zip_contents_checked(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'source.zip'
            with ZipFile(path,'w') as archive:archive.writestr('source/AGENTS.md','local')
            with self.assertRaises(ValueError):assert_archive(path)
    def test_runtime_or_installer_payload_checked(self):
        with tempfile.TemporaryDirectory() as folder:
            (Path(folder)/'AGENTS.md').write_text('local')
            with self.assertRaises(ValueError):assert_tree(folder)
    def test_source_enumerator_excludes_local_entrypoint(self):
        spec=importlib.util.spec_from_file_location('release',Path(__file__).with_name('build-release.py'));release=importlib.util.module_from_spec(spec);spec.loader.exec_module(release)
        names=[p.relative_to(release.ROOT).as_posix() for p in release.source_files()]
        assert_names(names);self.assertFalse(any(n.lower().endswith('agents.md') for n in names));self.assertIn('src/EqlTracker.App/OverviewWindow.cs',names)

if __name__=='__main__':unittest.main()
