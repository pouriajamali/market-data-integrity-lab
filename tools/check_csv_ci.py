"""Run the fixed offline CSV preview in a caller's workspace. Stdlib only."""
from pathlib import Path, PurePosixPath
import base64
import hashlib
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile

VERSION = 'v0.6.0-preview.1'
BASE = 'https://github.com/pouriajamali/market-data-integrity-lab/releases/download/' + VERSION + '/'
PUBLIC_KEY = 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE3HcCoaqX2eKglIpCpVS04stHolalzViq2mbU/Tp8Q/'
FINGERPRINT = 'AgU5bZlJSjUu0KsWMmVEEr+BfXpcMVNroTA33bHcQdU'
ARCHIVES = {
    'Windows': ('market-data-integrity-lab-v0.6.0-preview.1-win-x64.zip', '50cbfb1d2d6342cc9ce50e085b3cecae3e853549057dea8d826acd6cf64128a6'),
    'Linux': ('market-data-integrity-lab-v0.6.0-preview.1-linux-x64.tar.gz', 'a7be0affb7fa7df0cf9645772ffdd3fdd6d42e129d6560c08de786880708288f'),
}
CSV_CAP = 64 * 1024 * 1024
DOWNLOAD_CAP = 128 * 1024 * 1024
EXPANDED_CAP = 512 * 1024 * 1024

def scale(value):
    if not re.fullmatch('[0-8]', value):
        raise ValueError('Set each decimal scale explicitly to one digit from 0 through 8.')
    return value

def workspace_path(root, value):
    if not value or any(c in value for c in '\r\n\0'):
        raise ValueError('Use a nonempty workspace path without control characters.')
    result = (root / value).resolve()
    if not result.is_relative_to(root) or result == root:
        raise ValueError('Input and report paths must remain inside the workspace.')
    return result

def download(name, destination, limit):
    request = urllib.request.Request(BASE + name, headers={'User-Agent': 'MarketDataIntegrityLab-CSV-Action/0.1'})
    count = 0
    with urllib.request.urlopen(request, timeout=30) as response, destination.open('xb') as target:
        while chunk := response.read(65536):
            count += len(chunk)
            if count > limit:
                raise ValueError('Download exceeded its fixed size limit.')
            target.write(chunk)

def verify_manifest(manifest, signature, temporary):
    raw_key = base64.b64decode(PUBLIC_KEY.split()[1], validate=True)
    fingerprint = base64.b64encode(hashlib.sha256(raw_key).digest()).decode().rstrip('=')
    if fingerprint != FINGERPRINT:
        raise ValueError('Signing-key pin does not match.')
    allowed = temporary / 'allowed-signers'
    allowed.write_text('pouriajamali ' + PUBLIC_KEY + '\n', encoding='ascii')
    result = subprocess.run(['ssh-keygen', '-Y', 'verify', '-f', str(allowed), '-I', 'pouriajamali',
                             '-n', 'file', '-s', str(signature)], input=manifest.read_bytes(),
                            capture_output=True, timeout=30)
    if result.returncode:
        raise ValueError('Release manifest signature verification failed.')

def member_path(name):
    path = PurePosixPath(name)
    if not name or '\\' in name or ':' in name or path.is_absolute() or '..' in path.parts:
        raise ValueError('Unsafe archive member path.')
    return path

def extract(archive, destination):
    total = 0
    if archive.name.endswith('.zip'):
        with zipfile.ZipFile(archive) as source:
            for info in source.infolist():
                member_path(info.filename)
                if (info.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError('Archive links are not supported.')
                total += info.file_size
                if total > EXPANDED_CAP:
                    raise ValueError('Expanded archive exceeded its fixed size limit.')
            source.extractall(destination)
    else:
        with tarfile.open(archive, 'r:gz') as source:
            for info in source.getmembers():
                member_path(info.name)
                if not (info.isfile() or info.isdir()):
                    raise ValueError('Archive links and special files are not supported.')
                total += info.size
                if total > EXPANDED_CAP:
                    raise ValueError('Expanded archive exceeded its fixed size limit.')
            source.extractall(destination, filter='data')

def run():
    root = Path(os.environ.get('GITHUB_WORKSPACE', '.')).resolve()
    csv = workspace_path(root, os.environ.get('LAB_CSV', ''))
    report = workspace_path(root, os.environ.get('LAB_REPORT_DIRECTORY', 'artifacts/ohlc-check'))
    price = scale(os.environ.get('LAB_PRICE_DECIMALS', ''))
    volume = scale(os.environ.get('LAB_VOLUME_DECIMALS', ''))
    if not csv.is_file() or not 0 < csv.stat().st_size <= CSV_CAP:
        raise ValueError('CSV must be a nonempty local file of at most 64 MiB.')
    if report.exists():
        raise ValueError('The report directory already exists; use a new destination.')
    system = platform.system()
    if system not in ARCHIVES or platform.machine().lower() not in ('amd64', 'x86_64'):
        raise ValueError('This action supports Windows x64 and Linux glibc x64 only.')
    name, expected = ARCHIVES[system]
    with tempfile.TemporaryDirectory(prefix='ohlc-preview-') as temp:
        temporary = Path(temp)
        manifest, signature, archive = [temporary / value for value in ('SHA256SUMS.txt', 'SHA256SUMS.txt.sig', name)]
        download(manifest.name, manifest, 16384)
        download(signature.name, signature, 16384)
        verify_manifest(manifest, signature, temporary)
        entries = dict(line.split(None, 1)[::-1] for line in manifest.read_text(encoding='ascii').splitlines() if line.strip())
        if entries.get(name) != expected:
            raise ValueError('Signed manifest disagrees with the pinned archive hash.')
        download(name, archive, DOWNLOAD_CAP)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
            raise ValueError('Downloaded archive checksum mismatch.')
        extract(archive, temporary / 'app')
        executable = 'Integrity.exe' if system == 'Windows' else 'Integrity'
        candidates = list((temporary / 'app').rglob(executable))
        if len(candidates) != 1:
            raise ValueError('Expected one extracted apphost.')
        report.parent.mkdir(parents=True, exist_ok=True)
        result = subprocess.run([str(candidates[0]), 'validate-csv', str(csv), str(report), price, volume],
                                capture_output=True, timeout=300)
        # Report only the outcome here; do not copy input rows to the job log.
        code = result.returncode if result.returncode in (0, 1, 2) else 1
        print(f'Offline CSV result: exit {code}; reports remain in the requested workspace directory.')
        return code

def main():
    code = 1
    try:
        code = run()
    except (ValueError, OSError, subprocess.SubprocessError, zipfile.BadZipFile, tarfile.TarError) as error:
        print('CSV action refused or could not complete: ' + str(error), file=sys.stderr)
    finally:
        output = os.environ.get('GITHUB_OUTPUT')
        if output:
            with open(output, 'a', encoding='utf8') as stream:
                stream.write(f'exit-code={code}\n')
    return code

if __name__ == '__main__':
    sys.exit(main())
