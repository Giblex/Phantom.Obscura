#!/usr/bin/env python3
"""
Validate the parts of AXAML that Avalonia parses at RUNTIME, not at build time.

Some AXAML attribute values are compiled to a string and only parsed when the
control is constructed. A malformed value therefore produces a completely clean
build and then throws the moment the window is opened. Two production crashes in
this codebase came from exactly that:

  * BoxShadow="0 20 60 -8 #9000000000"   -> 10 hex digits; Color.Parse throws
  * Easing="QuadraticOut"                -> not a real easing; ctor throws

Both were invisible to the compiler. This script closes that gap.

Checks:
  1. Easing names resolve to Avalonia.Animation.Easings (or a cubic-bezier spec).
  2. BoxShadow colours are 3, 4, 6 or 8 hex digits.
  3. avares:// asset URIs point at files that actually exist.

Exit code 0 = clean, 1 = problems found.
"""

import pathlib
import re
import sys

# Avalonia.Animation.Easings. Verified against Avalonia.Base.dll 11.3.11.
EASING_KINDS = [
    "Back", "Bounce", "Circular", "Cubic", "Elastic",
    "Exponential", "Quadratic", "Quartic", "Quintic", "Sine",
]
VALID_EASINGS = {"LinearEasing"} | {
    f"{kind}Ease{direction}"
    for kind in EASING_KINDS
    for direction in ("In", "Out", "InOut")
}

# Avalonia also accepts a cubic-bezier control-point spec, e.g. "0.16,1,0.3,1".
SPLINE_EASING = re.compile(r"^\s*[-\d.]+\s*,\s*[-\d.]+\s*,\s*[-\d.]+\s*,\s*[-\d.]+\s*$")

VALID_HEX_DIGIT_COUNTS = {3, 4, 6, 8}


def check_file(path: pathlib.Path, project_root: pathlib.Path) -> list[str]:
    text = path.read_text(encoding="utf-8", errors="replace")
    problems: list[str] = []

    def line_of(index: int) -> int:
        return text.count("\n", 0, index) + 1

    for match in re.finditer(r'Easing="([^"]+)"', text):
        value = match.group(1)
        if value not in VALID_EASINGS and not SPLINE_EASING.match(value):
            problems.append(
                f"{path}:{line_of(match.start())}: unknown easing {value!r}. "
                f"Valid names look like 'QuadraticEaseOut' (not 'QuadraticOut')."
            )

    for match in re.finditer(r'BoxShadow(?:es)?\s*=\s*"([^"]*)"', text):
        # A BoxShadow attribute may hold several comma-separated shadows.
        for shadow in match.group(1).split(","):
            for token in (t.strip().rstrip(",") for t in shadow.split()):
                if not token.startswith("#"):
                    continue
                digits = len(token) - 1
                if digits not in VALID_HEX_DIGIT_COUNTS:
                    problems.append(
                        f"{path}:{line_of(match.start())}: BoxShadow colour {token} "
                        f"has {digits} hex digits; expected 3, 4, 6 or 8 (#AARRGGBB)."
                    )

    # Asset paths can contain spaces, so match through to the closing quote.
    #
    # Only URIs targeting THIS project's own assembly can be resolved against this
    # directory. A reference such as avares://Phantom.UI.Shared/Icons/VectorIcons.axaml
    # lives in a different assembly and is not ours to verify — checking it here just
    # produces a false failure.
    own_assembly = local_assembly_name(project_root)
    for match in re.finditer(r'avares://([A-Za-z0-9_.]+)/([^"\']+)["\']', text):
        assembly, rel = match.group(1), match.group(2).strip()
        if own_assembly is None or assembly != own_assembly:
            continue
        if not (project_root / rel).exists():
            problems.append(
                f"{path}:{line_of(match.start())}: asset not found: {rel!r}"
            )

    return problems


def local_assembly_name(project_root: pathlib.Path) -> str | None:
    """Assembly name for the project in this directory, from its .csproj."""
    projects = list(project_root.glob("*.csproj"))
    if not projects:
        return None

    text = projects[0].read_text(encoding="utf-8", errors="replace")
    explicit = re.search(r"<AssemblyName>\s*([^<]+?)\s*</AssemblyName>", text)
    if explicit:
        return explicit.group(1)

    # Defaults to the project file name when AssemblyName is not set.
    return projects[0].stem


def main() -> int:
    # Default to the desktop UI project, where the AXAML lives.
    root = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path("src/UI.Desktop")
    if not root.exists():
        print(f"validate-runtime-xaml: path not found: {root}", file=sys.stderr)
        return 1

    files = [
        p for p in root.rglob("*.axaml")
        if "bin" not in p.parts and "obj" not in p.parts
    ]

    problems: list[str] = []
    for path in files:
        problems.extend(check_file(path, root))

    for problem in problems:
        print(problem)

    print(f"\nvalidate-runtime-xaml: {len(files)} file(s) checked, {len(problems)} problem(s).")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
