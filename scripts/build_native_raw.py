"""Build only the small RAW bridge against an explicitly selected installed LibRaw SDK."""
import argparse
import pathlib
import platform
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--libraw-root', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
source = pathlib.Path(__file__).resolve().parents[1] / 'native/PhotoLab.NativeRaw/session.cpp'
root = args.libraw_root.resolve()
if platform.system() == 'Windows':
    target = args.output.resolve() / 'PhotoLab.NativeRaw.dll'
    command = ['cl', '/nologo', '/std:c++17', '/EHsc', '/O2', '/LD', '/DLIBRAW_WIN32_UNICODEPATHS',
               '/I' + str(root / 'include'), str(source), str(root / 'lib/libraw_r.lib'),
               '/link', '/OUT:' + str(target)]
else:
    mac = platform.system() == 'Darwin'
    target = args.output.resolve() / ('libPhotoLab.NativeRaw.dylib' if mac else 'libPhotoLab.NativeRaw.so')
    command = ['clang++' if mac else 'c++', '-std=c++17', '-O2', '-dynamiclib' if mac else '-shared',
               '-fPIC', '-fvisibility=hidden', '-I' + str(root / 'include'), str(source),
               '-L' + str(root / 'lib'), '-lraw_r', '-Wl,-rpath,' + str(root / 'lib'), '-o', str(target)]
subprocess.run(command, cwd=args.output, check=True)
print(target)
