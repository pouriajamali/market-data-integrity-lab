"""Contract checks for the CI adapter; CLI semantics remain covered by C# tests."""
from pathlib import Path
import io
import tempfile
import unittest
import zipfile
import check_csv_ci as action

class AdapterTests(unittest.TestCase):
    def test_scales_are_explicit_single_digits(self):
        for valid in ('0','2','8'): self.assertEqual(action.scale(valid),valid)
        for bad in ('','-1','9','2.0','02','2\n','2;echo secret'):
            with self.assertRaises(ValueError): action.scale(bad)

    def test_paths_cannot_escape_workspace_or_use_it_as_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve()
            self.assertEqual(action.workspace_path(root,'data/bars.csv'), root/'data/bars.csv')
            for bad in ('../outside.csv','.','x\nvalue',''):
                with self.assertRaises(ValueError): action.workspace_path(root,bad)

    def test_archive_traversal_and_links_are_rejected_before_extraction(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            with self.assertRaises(ValueError): action.member_path('a\\b')
            for index,entry in enumerate(('../escape','/absolute','drive:c','link')):
                archive=root/f'{index}.zip'
                with zipfile.ZipFile(archive,'w') as output:
                    item=zipfile.ZipInfo(entry)
                    if entry=='link': item.external_attr=0o120777<<16
                    output.writestr(item,b'target')
                with self.subTest(entry=entry):
                    with self.assertRaises(ValueError): action.extract(archive,root/'extracted')
            self.assertFalse((root/'extracted').exists())

    def test_public_key_pin_matches(self):
        import base64,hashlib
        raw=base64.b64decode(action.PUBLIC_KEY.split()[1],validate=True)
        self.assertEqual(base64.b64encode(hashlib.sha256(raw).digest()).decode().rstrip('='),action.FINGERPRINT)

if __name__=='__main__': unittest.main()
