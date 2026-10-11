"""Stage relocatable macOS color libraries and their notices beside an already published app."""
import argparse
import hashlib
import json
import pathlib
import platform
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--png-prefix", required=True, type=pathlib.Path)
parser.add_argument("--lcms-prefix", required=True, type=pathlib.Path)
parser.add_argument("--directory", required=True, type=pathlib.Path)
args = parser.parse_args()
if platform.system() != "Darwin":
    parser.error("This packaging recipe requires macOS tools; Windows packaging is not implemented.")
root = pathlib.Path(__file__).resolve().parents[1]
directory = args.directory.resolve()
png_prefix = args.png_prefix.resolve()
lcms_prefix = args.lcms_prefix.resolve()
if directory.is_relative_to(png_prefix) or directory.is_relative_to(lcms_prefix):
    parser.error("Package output must be outside the native SDK installations.")
directory.mkdir(parents=True, exist_ok=True)
libraries = {
    "libpng16.16.dylib": png_prefix / "lib/libpng16.16.dylib",
    "liblcms2.dylib": lcms_prefix / "lib/liblcms2.2.dylib",
}
# Validate all external artifacts before changing the destination.
for source in [*libraries.values(), png_prefix / "LICENSE", lcms_prefix / "LICENSE"]:
    if not source.is_file():
        parser.error("Missing native SDK artifact: " + str(source))
bridge = directory / "libPhotoLab.NativePng.dylib"
subprocess.run([sys.executable, str(root / "scripts/build_native_png.py"), "--prefix", str(png_prefix),
                "--output", str(bridge)], check=True)
for name, source in libraries.items():
    shutil.copyfile(source, directory / name)
# Rewrite only staged copies. The SDK installation is never modified.
for name in [bridge.name, *libraries]:
    subprocess.run(["install_name_tool", "-id", "@rpath/" + name, str(directory / name)], check=True)
dependencies = subprocess.check_output(["otool", "-L", str(bridge)], text=True).splitlines()[1:]
for line in dependencies:
    dependency = line.strip().split(" (", 1)[0]
    if pathlib.Path(dependency).name == "libpng16.16.dylib":
        subprocess.run(["install_name_tool", "-change", dependency,
                        "@loader_path/libpng16.16.dylib", str(bridge)], check=True)
subprocess.run(["install_name_tool", "-delete_rpath", str(png_prefix / "lib"), str(bridge)], check=True)
for name in [bridge.name, *libraries]:
    subprocess.run(["lipo", str(directory / name), "-verify_arch", platform.machine()], check=True)
    subprocess.run(["codesign", "--force", "--sign", "-", str(directory / name)], check=True)
    dependencies = subprocess.check_output(["otool", "-L", str(directory / name)], text=True)
    for line in dependencies.splitlines()[1:]:
        dependency = line.strip().split(" (", 1)[0]
        if not dependency.startswith(("@loader_path/", "@rpath/", "/usr/lib/", "/System/Library/")):
            raise RuntimeError("Unbundled native dependency: " + dependency)
notices = directory / "licenses"
notices.mkdir(exist_ok=True)
shutil.copyfile(png_prefix / "LICENSE", notices / "libpng-LICENSE.txt")
shutil.copyfile(lcms_prefix / "LICENSE", notices / "LittleCMS-LICENSE.txt")
artifacts = [bridge.name, *libraries, "licenses/libpng-LICENSE.txt", "licenses/LittleCMS-LICENSE.txt"]
manifest = {"platform": "macOS", "architecture": platform.machine(),
            "scope": "Color libraries only; ad-hoc signed, not notarized or a complete commercial installer",
            "sha256": {name: hashlib.sha256((directory / name).read_bytes()).hexdigest() for name in artifacts}}
(directory / "color-runtime.json").write_text(json.dumps(manifest, indent=2) + "\n")
print(directory)
