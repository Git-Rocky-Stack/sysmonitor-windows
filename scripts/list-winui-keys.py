#!/usr/bin/env python3
"""Lists the resource keys WinUI provides, for the tests that check the app's own resources against them.

XamlControlsResources, merged first in App.xaml, brings every resource in the Windows App SDK's theme
dictionary into the application. So a StaticResource or ThemeResource naming one of those keys resolves, and a
resource the app defines under one of those names replaces WinUI's for every control that reads it - on purpose
in a Fluent override, by accident anywhere else. The tests need the real list to tell the two apart, and the
XAML compiler checks neither.

The list is every x:Key in the package's lib/uap10.0/Microsoft.UI/Themes/generic.xaml, plus the keys that file
uses without defining: the system colours (SystemColorWindowColor, SystemAccentColor and their kin) and a few
styles the framework supplies from its own binary. It is written, sorted, to
tests/SysMonitor.Tests/TestSupport/winui-resource-keys.txt, under a header naming the package version, and a
test fails when that version is not the one SysMonitor.App.csproj references - so upgrading the Windows App SDK
means running this again.

Usage:
    python scripts/list-winui-keys.py [--package PATH]

PATH is the package as NuGet restores it (a folder) or the .nupkg itself. By default it is the version the app
project references, in the NuGet packages folder (NUGET_PACKAGES, or ~/.nuget/packages), which any build of the
app fills.
"""

import argparse
import os
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

REPO = pathlib.Path(__file__).resolve().parent.parent
PROJECT = REPO / "src" / "SysMonitor.App" / "SysMonitor.App.csproj"
OUTPUT = REPO / "tests" / "SysMonitor.Tests" / "TestSupport" / "winui-resource-keys.txt"
PACKAGE = "Microsoft.WindowsAppSDK"
THEME_FILE = "lib/uap10.0/Microsoft.UI/Themes/generic.xaml"

XAML = "{http://schemas.microsoft.com/winfx/2006/xaml}"
REFERENCE = re.compile(r"\{(?:StaticResource|ThemeResource)\s+(?:ResourceKey\s*=\s*)?([^\s,{}]+)\s*\}")


def referenced_version():
    """The Windows App SDK version the app project builds with."""
    project = ET.parse(PROJECT).getroot()
    for reference in project.iter("PackageReference"):
        if reference.get("Include") == PACKAGE:
            return reference.get("Version")
    sys.exit(f"{PROJECT.relative_to(REPO)} references no {PACKAGE} package")


def read_theme_file(package):
    """generic.xaml from a restored package folder or a .nupkg."""
    if package.is_file():
        with zipfile.ZipFile(package) as archive:
            try:
                return archive.read(THEME_FILE)
            except KeyError:
                sys.exit(f"{package} holds no {THEME_FILE}")

    theme_file = package / THEME_FILE
    if not theme_file.is_file():
        sys.exit(f"{theme_file} does not exist - build the app once, or pass --package")
    return theme_file.read_bytes()


def keys_in(theme_xaml):
    """Every key the file defines, and every key it asks for without defining."""
    root = ET.fromstring(theme_xaml)
    # A theme dictionary's own key (Default, Light, HighContrast) names a theme, not a resource.
    themes = {id(theme) for owner in root.iter() if owner.tag.endswith("ResourceDictionary.ThemeDictionaries")
              for theme in owner}
    defined = set()
    used = set()
    for element in root.iter():
        key = element.get(XAML + "Key")
        if key and id(element) not in themes:
            defined.add(key)
        for value in element.attrib.values():
            used.update(REFERENCE.findall(value))
        if element.tag.endswith("}StaticResource") and element.get("ResourceKey"):
            used.add(element.get("ResourceKey"))
    return defined, used - defined


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--package", type=pathlib.Path,
                        help=f"the restored {PACKAGE} folder or .nupkg (default: the NuGet packages folder)")
    args = parser.parse_args()

    version = referenced_version()
    package = args.package
    if package is None:
        packages = pathlib.Path(os.environ.get("NUGET_PACKAGES", pathlib.Path.home() / ".nuget" / "packages"))
        package = packages / PACKAGE.lower() / version
    elif version not in package.name:
        sys.exit(f"{package} does not look like {PACKAGE} {version}, the version {PROJECT.name} references")

    defined, supplied = keys_in(read_theme_file(package))
    header = [
        "# The resource keys WinUI provides: every x:Key in the Windows App SDK's theme dictionary, and the keys",
        "# that dictionary uses without defining (the system colours, and a few styles the framework supplies itself).",
        f"# Written by scripts/list-winui-keys.py from {THEME_FILE}; run it again when the version changes.",
        f"# {PACKAGE} {version}",
    ]
    lines = header + sorted(defined | supplied, key=lambda key: (key.lower(), key))
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="ascii", newline="\n")
    print(f"{len(defined)} keys defined and {len(supplied)} supplied by the framework, "
          f"written to {OUTPUT.relative_to(REPO)}")


if __name__ == "__main__":
    main()
