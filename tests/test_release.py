import json,sys,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from release import find_release

class ReleaseTests(unittest.TestCase):
    def test_finds_unpublished_draft_without_tag_endpoint(self):
        draft={'tag_name':'v1.0.0-beta.1','draft':True,'id':123}
        with patch('release.gh',return_value=json.dumps([[{'tag_name':'v0.1.0'}],[draft]]).encode()) as call:
            self.assertEqual(find_release('owner/tool','v1.0.0-beta.1'),draft)
            self.assertIn('--paginate',call.call_args.args)
            self.assertNotIn('/tags/',call.call_args.args[-1])

if __name__=='__main__':unittest.main()
