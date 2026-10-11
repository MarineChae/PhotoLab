"""Download CC0 camera fixtures and the mislabeled negative sample; verify repository SHA-256 hashes."""

import hashlib
import json
from pathlib import Path
import sys
import tempfile
import urllib.parse
import urllib.request

manifest = Path(__file__).resolve().parent.parent / "tests/fixtures/raw/manifest.json"
destination = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(tempfile.gettempdir()) / "photolab-raw-samples"
destination.mkdir(parents=True, exist_ok=True)
repository = json.loads(manifest.read_text())
for sample in repository["samples"] + repository["negative_samples"]:
    path = destination / sample["file"]
    if path.exists() and hashlib.sha256(path.read_bytes()).hexdigest() == sample["sha256"]:
        print("Cached:", path, flush=True)
        continue
    url = urllib.parse.quote(sample["url"], safe=":/?=&")
    request = urllib.request.Request(url, headers={"User-Agent": "PhotoLab-regression-tests/0.1"})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != sample["sha256"]:
        raise ValueError("Camera fixture checksum mismatch: " + sample["file"])
    path.write_bytes(data)
    print("Downloaded:", path, sample["make"], sample["model"], flush=True)
