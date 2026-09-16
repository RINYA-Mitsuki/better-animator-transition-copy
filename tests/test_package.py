import sys, unittest, tempfile, json, zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from package import build,validate_version

class PackageTests(unittest.TestCase):
    def test_semver_rejects_ambiguous_tags(self):
        for version in ('1.0','v1.0.0','01.0.0','1.0.0-beta.01','1.0.0;echo x'):
            with self.assertRaises(ValueError): validate_version(version)
        for version in ('1.0.0','1.2.3-beta.1'): validate_version(version)

    def test_archive_is_repeatable_and_has_manifest_at_root(self):
        root=Path(__file__).resolve().parents[1]/'Packages/com.mitsuboshi-studio.better-animator-transition-copy'
        with tempfile.TemporaryDirectory() as a,tempfile.TemporaryDirectory() as b:
            m,z,h=build(root,a,'RINYA-Mitsuki/better-animator-transition-copy')
            self.assertEqual(h,build(root,b,'RINYA-Mitsuki/better-animator-transition-copy')[2])
            with zipfile.ZipFile(z) as archive:
                self.assertEqual(json.loads(archive.read('package.json')),m)
                self.assertIn('Editor/BetterAnimatorTransitionCopy.cs',archive.namelist())
                self.assertFalse(any(n.startswith(('Packages/','.github/','scripts/')) for n in archive.namelist()))

if __name__=='__main__': unittest.main()
