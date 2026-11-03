"""Read the already tested archives; never rebuild binaries in the release job."""
import hashlib
import json
from pathlib import Path
import sys
import tarfile
import zipfile

root=Path(sys.argv[1]);commit=sys.argv[2]
expected={"win-x64":".zip","linux-x64":".tar.gz"}
assert len(list(root.iterdir()))==2,"Only two tested runtime archives expected"
for rid,suffix in expected.items():
    prefix=f"market-data-integrity-lab-v0.6.0-preview.1-{rid}"
    path=root/(prefix+suffix)
    if suffix==".zip":
        with zipfile.ZipFile(path) as archive:
            files={i.filename:archive.read(i) for i in archive.infolist() if not i.is_dir()}
    else:
        with tarfile.open(path) as archive:
            assert all(i.isfile() or i.isdir() for i in archive.getmembers())
            files={i.name:archive.extractfile(i).read() for i in archive.getmembers() if i.isfile()}
    manifest=json.loads(files[prefix+"/BUILD-MANIFEST.json"])
    assert manifest["sourceCommit"]==commit and manifest["rid"]==rid and manifest["selfContained"] is True
    assert manifest["version"]=="0.6.0-preview.1" and manifest["bundledRuntime"]=="10.0.14" and manifest["sdk"]=="10.0.403"
    expected_files={prefix+"/"+i["path"] for i in manifest["files"]}|{prefix+"/BUILD-MANIFEST.json"}
    assert set(files)==expected_files
    for row in manifest["files"]:
        data=files[prefix+"/"+row["path"]]
        assert len(data)==row["bytes"] and hashlib.sha256(data).hexdigest()==row["sha256"]
print("Both same-commit tested runtime archives verified; no rebuild.")
