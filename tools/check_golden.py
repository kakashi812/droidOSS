"""Check that the C#, Kotlin and Python implementations agree on the golden packet.

Each implementation already tests itself against the golden INPUT packet, but
each test carries its own copy of the expected bytes. If someone "fixes" one copy
to match a changed encoder, that language's tests stay green while the protocol
has quietly forked. This script reads all three copies and fails unless they are
byte-for-byte identical.

    py tools/check_golden.py

Standard library only. Exit code 0 means all three agree.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

import fake_phone  # noqa: E402  (needs the path above)

CSHARP = ROOT / "server" / "DroidOSS.Tests" / "InputPacketTests.cs"
KOTLIN = (ROOT / "androidapp3" / "app" / "src" / "test" / "java" / "io" / "github"
          / "kakashi812" / "droidoss" / "protocol" / "PacketCodecTest.kt")


def from_csharp() -> str:
    source = CSHARP.read_text(encoding="utf-8")
    block = re.search(r"GoldenBytes\s*=\s*\[(.*?)\];", source, re.S)
    if block is None:
        sys.exit(f"GoldenBytes not found in {CSHARP}")
    # Drop the // comments first: they contain numbers too.
    code = re.sub(r"//[^\n]*", "", block.group(1))
    return "".join(b[2:].lower() for b in re.findall(r"0x[0-9A-Fa-f]{2}", code))


def from_kotlin() -> str:
    source = KOTLIN.read_text(encoding="utf-8")
    test = source.split("input packet matches the golden vector", 1)[1]
    expected = re.search(r'assertEquals\("([0-9a-f ]+)"', test)
    if expected is None:
        sys.exit(f"golden hex string not found in {KOTLIN}")
    return expected.group(1).replace(" ", "")


def from_python() -> str:
    return fake_phone.golden_packet().hex()


def main() -> int:
    found = {"C#": from_csharp(), "Kotlin": from_kotlin(), "Python": from_python()}
    for language, value in found.items():
        print(f"  {language:<7} {value}")

    if len(set(found.values())) != 1:
        print("FAIL: the golden packet differs between implementations")
        return 1
    if len(found["Python"]) != 2 * 20:
        print("FAIL: the golden packet is not 20 bytes")
        return 1

    print("OK: all three implementations agree")
    return 0


if __name__ == "__main__":
    sys.exit(main())
