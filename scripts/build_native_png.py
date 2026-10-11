"""Build the narrow PNG16 bridge against an explicitly selected libpng SDK."""
import argparse
import pathlib
import platform
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--prefix", required=True, type=pathlib.Path)
parser.add_argument("--output", required=True, type=pathlib.Path)
parser.add_argument("--library", type=pathlib.Path, help="Windows libpng import library (default: prefix/lib/libpng16.lib)")
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
args.output.parent.mkdir(parents=True, exist_ok=True)
prefix = args.prefix.resolve()
output = args.output.resolve()
source = root / "native/PhotoLab.NativePng/decode.c"
if platform.system() == "Windows":
    library = (args.library or prefix / "lib/libpng16.lib").resolve()
    command = ["cl", "/nologo", "/std:c11", "/O2", "/W4", "/LD", "/I" + str(prefix / "include"),
               str(source), "/link", str(library), "/OUT:" + str(output)]
else:
    mac = platform.system() == "Darwin"
    command = ["clang" if mac else "cc", "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror", "-fPIC",
               "-fvisibility=hidden", "-dynamiclib" if mac else "-shared", str(source),
               "-I" + str(prefix / "include"), "-L" + str(prefix / "lib"), "-lpng16",
               "-Wl,-rpath," + str(prefix / "lib"), "-o", str(output)]
subprocess.run(command, cwd=output.parent, check=True)
print(output)
