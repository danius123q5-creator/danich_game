"""Check that a release is installable by the shipped ZombieShooter launcher."""
import argparse
from pathlib import Path
import re


def launcher_installer(version):
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,3}", version):
        raise ValueError("Expected a numeric game release tag")
    return f"ZombieShooterSetup_{version}.exe"


def require_launcher_installer(version, filenames):
    required = launcher_installer(version)
    if required not in filenames:
        raise ValueError(f"Launcher compatibility requires {required}")
    return required


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("assets", nargs="+", type=Path)
    args = parser.parse_args()
    if any(not path.is_file() or path.stat().st_size == 0 for path in args.assets):
        parser.error("All assets must be non-empty files")
    try:
        print(require_launcher_installer(args.version, [path.name for path in args.assets]))
    except ValueError as error:
        parser.error(str(error))


if __name__ == "__main__":
    main()
