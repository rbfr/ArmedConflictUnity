#!/usr/bin/env python3
"""Unpack TextMesh Pro's essential resources into Assets/.

TMP will not render a single glyph without its settings asset and default LiberationSans SDF
font. Unity normally imports them through a modal popup the first time a TMP object is created —
which this project can never see, because the editor GUI runs over VNC and every build step is
-batchmode. AssetDatabase.ImportPackage is no help either: it is asynchronous, so under -quit the
editor shuts down before a single file lands (tried, and it silently imports nothing).

A .unitypackage is a gzipped tar of one directory per asset, each holding `asset` (the payload),
`asset.meta` (which carries the GUID) and `pathname` (where it belongs). Unpacking it directly is
deterministic and needs no editor at all.

This is a ONE-TIME step whose output is committed. Run it again only if Assets/TextMesh Pro is
lost, or to pick up a newer com.unity.ugui.

    python3 tools/import_tmp_essentials.py
"""

import glob
import os
import sys
import tarfile

PACKAGE = "Package Resources/TMP Essential Resources.unitypackage"


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    # The package folder carries a build hash, so it cannot be hardcoded.
    matches = glob.glob(os.path.join(root, "Library/PackageCache/com.unity.ugui@*", PACKAGE))
    if not matches:
        print("error: TMP Essential Resources.unitypackage not found in the package cache.")
        print("       Open the project once so Unity resolves com.unity.ugui, then re-run.")
        return 1
    package = matches[0]

    written = 0
    with tarfile.open(package, "r:gz") as tar:
        entries = {}
        for member in tar.getmembers():
            if not member.isfile():
                continue
            folder, _, kind = member.name.rpartition("/")
            entries.setdefault(folder, {})[kind] = member

        for folder, files in sorted(entries.items()):
            # A directory entry carries a pathname but no asset. Unity recreates those itself.
            if "pathname" not in files or "asset" not in files:
                continue

            # pathname is stored WITHOUT a trailing newline in some packages and with one in
            # others, and may carry a second line (the original path) after a newline.
            pathname = tar.extractfile(files["pathname"]).read().decode("utf8")
            pathname = pathname.split("\n")[0].strip()
            if not pathname.startswith("Assets/"):
                continue

            dest = os.path.join(root, pathname)
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with open(dest, "wb") as f:
                f.write(tar.extractfile(files["asset"]).read())

            # The .meta must come across too — it holds the GUID every reference resolves
            # through. Regenerated metas would give TMP's shaders and font new GUIDs and break
            # the material references inside the assets we just wrote.
            if "asset.meta" in files:
                with open(dest + ".meta", "wb") as f:
                    f.write(tar.extractfile(files["asset.meta"]).read())
            written += 1

    print(f"wrote {written} assets to Assets/TextMesh Pro")
    settings = os.path.join(root, "Assets/TextMesh Pro/Resources/TMP Settings.asset")
    if not os.path.exists(settings):
        print("error: TMP Settings.asset did not land — TMP will render nothing")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
