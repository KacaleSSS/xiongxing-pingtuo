"""Package the universal Mac app using both native input engines and discovery helpers."""
import argparse
import os
from pathlib import Path
import plistlib
import struct
import zipfile

ROOT = Path(__file__).resolve().parent
PREFIX = "雄兴屏拓.app/Contents/"

def pack(inputs, output):
    def universal(stem):
        image=bytearray(struct.pack(">II",0xcafebabe,2)+bytes(40))
        for i,(cpu,sub,name) in enumerate([(0x100000c,0,"arm64"),(0x1000007,3,"amd64")]):
            data=(inputs/(stem+"-"+name)).read_bytes()
            if struct.unpack_from("<II",data)!=(0xfeedfacf,cpu):raise ValueError("wrong architecture: "+stem+"-"+name)
            image.extend(bytes((-len(image))%16384));struct.pack_into(">IIIII",image,8+20*i,cpu,sub,len(image),len(data),14)
            image.extend(data)
        return image
    icon=(ROOT.parent.parent/"Branding/app.icns").read_bytes()
    temp = output.with_suffix(".building")
    def add(z, name, data, mode=0o644):
        item = zipfile.ZipInfo(PREFIX + name)
        item.create_system = 3
        item.external_attr = (0o100000 | mode) << 16
        z.writestr(item, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    try:
        with zipfile.ZipFile(temp, "w") as z:
            add(z, "Info.plist", plistlib.dumps({
                "CFBundlePackageType": "APPL", "CFBundleExecutable": "desktop-link",
                "CFBundleIdentifier": "local.edgemouse.desktoplink", "CFBundleName": "雄兴屏拓",
                "CFBundleDisplayName":"雄兴屏拓", "CFBundleVersion": "6", "CFBundleShortVersionString": "6.0", "CFBundleIconFile":"ZZXXCloudTech.icns", "LSMinimumSystemVersion":"12.0",
                "NSLocalNetworkUsageDescription": "在同一局域网发现配套的 Windows 电脑并连接键鼠。"
            }))
            add(z, "MacOS/desktop-link", universal("ui"), 0o755)
            add(z,"Resources/ZZXXCloudTech.icns",icon)
            add(z,"Helpers/zzxx-input",universal("engine"),0o755)
            add(z,"Resources/lan-mouse-engine-source.zip",(ROOT.parent/"engine/lan-mouse-engine-source.zip").read_bytes())
            for arch in ("arm64","intel"):
                helper = (inputs/("pair-arm64" if arch == "arm64" else "pair-amd64")).read_bytes()
                if struct.unpack_from("<II", helper) != (0xfeedfacf, 0x100000c if arch == "arm64" else 0x1000007):
                    raise ValueError("wrong Mach-O architecture for " + arch)
                add(z, "Helpers/pair-" + arch, helper, 0o755)
            for file in [ROOT.parent/"LICENSE-Lan-Mouse.txt",ROOT/"LICENSE-TOML.txt",ROOT/"LICENSE-Go.txt"]:
                add(z, "Resources/" + file.name, file.read_bytes())
        with zipfile.ZipFile(temp) as z:
            assert z.testzip() is None
            assert z.getinfo(PREFIX + "MacOS/desktop-link").external_attr >> 16 == 0o100755
            assert not any(n.endswith((".pem",".toml",".command")) for n in z.namelist())
        os.replace(temp, output)
    finally:
        temp.unlink(missing_ok=True)

if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("inputs",type=Path)
    args=parser.parse_args()
    pack(args.inputs,ROOT.parent/"Mac端.zip")
