#!/usr/bin/env python3
"""Generate the HTML documentation pages served by GitHub Pages.

The markdown files under docs/ are the source of truth. This script renders each
one into a sibling .html file that shares assets/style.css with index.html, so the
docs site is one design rather than ten.

Run it after editing any docs/*.md:

    python docs/build-docs.py

Check it in CI or before a release without writing anything:

    python docs/build-docs.py --check

--check exits 1 if any generated page is missing or out of date, which is what
stops a hand-edited .html from silently drifting away from its .md source.

Requires: markdown (pip install markdown)
"""

import argparse
import io
import os
import re
import sys

try:
    import markdown
except ImportError:
    sys.exit("This script needs the 'markdown' package: pip install markdown")

HERE = os.path.dirname(os.path.abspath(__file__))
SITE = "https://git-rocky-stack.github.io/sysmonitor-windows/"
REPO = "https://github.com/Git-Rocky-Stack/sysmonitor-windows"

# Every generated page: source markdown -> (kind shown in the eyebrow, meta description)
PAGES = {
    "tutorial-getting-started.md": (
        "Tutorial",
        "Install STX.1 System Monitor, read your system health score, free up disk "
        "space and save a diagnostic report, in about ten minutes."),
    "howto-free-disk-space.md": (
        "How-to guide",
        "Scan your PC for reclaimable files with STX.1 System Monitor, choose what "
        "goes category by category, and verify how much space you actually got back."),
    "howto-securely-wipe-files.md": (
        "How-to guide",
        "Overwrite a file's contents with STX.1 System Monitor, choose a wipe method, "
        "and understand what wiping does and does not guarantee on a solid-state drive."),
    "howto-back-up-and-restore.md": (
        "How-to guide",
        "Create an encrypted, verified backup with STX.1 System Monitor and restore "
        "files from it. Covers AES-256 encryption and what changed in version 3.0.0."),
    "howto-manage-startup-programs.md": (
        "How-to guide",
        "Stop programs launching at sign-in with STX.1 System Monitor, reversibly, "
        "using the same mechanism Task Manager writes."),
    "reference-pages.md": (
        "Reference",
        "Every one of the 34 pages in STX.1 System Monitor, what each one does, and "
        "the source line it is defined on."),
    "reference-settings-and-data.md": (
        "Reference",
        "Every setting in STX.1 System Monitor with its default value, and every "
        "location on disk the application writes to."),
    "explanation-honest-reporting.md": (
        "Explanation",
        "Why STX.1 System Monitor 3.0.0 removed four working features rather than "
        "rewording them, and the trade-offs that came with that decision."),
    "explanation-drive-wiper-and-ssds.md": (
        "Explanation",
        "Why overwriting a file on a solid-state drive cannot promise the data is "
        "unrecoverable, and what actually works instead."),
}

TEMPLATE = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<meta name="description" content="{description}">
<meta name="robots" content="index, follow">
<link rel="canonical" href="{canonical}">
<link rel="icon" href="assets/favicon.png" type="image/png">
<meta property="og:type" content="article">
<meta property="og:site_name" content="STX.1 System Monitor">
<meta property="og:title" content="{title}">
<meta property="og:description" content="{description}">
<meta property="og:url" content="{canonical}">
<meta property="og:image" content="{site}assets/screenshot-dashboard.png">
<meta property="og:locale" content="en_US">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="{title}">
<meta name="twitter:description" content="{description}">
<meta name="twitter:image" content="{site}assets/screenshot-dashboard.png">
<script type="application/ld+json">
{{
  "@context": "https://schema.org",
  "@type": "TechArticle",
  "headline": "{heading}",
  "description": "{description}",
  "url": "{canonical}",
  "inLanguage": "en",
  "isPartOf": {{ "@type": "WebSite", "url": "{site}", "name": "STX.1 System Monitor" }},
  "author": {{ "@type": "Organization", "name": "Rocky Stack", "url": "https://github.com/Git-Rocky-Stack" }},
  "about": {{ "@type": "SoftwareApplication", "name": "STX.1 System Monitor", "applicationCategory": "UtilitiesApplication", "operatingSystem": "Windows" }}
}}
</script>
<link rel="stylesheet" href="assets/style.css">
</head>
<body>
<a class="skip" href="#main">Skip to content</a>

<header class="site">
  <div class="wrap nav">
    <a class="brand" href="./"><span class="mark" aria-hidden="true">S1</span> STX.1 System Monitor</a>
    <nav class="navlinks" aria-label="Primary">
      <a href="./#download">Download</a>
      <a href="./#whatsnew">What is new</a>
      <a href="./#features">Features</a>
      <a href="./#docs">Documentation</a>
      <a href="{repo}">Source</a>
    </nav>
  </div>
</header>

<main id="main" class="doc">
  <div class="wrap">
    <p class="crumbs"><a href="./">Home</a> / <a href="./#docs">Documentation</a> / {kind}</p>
{body}
    <div class="docnav">
      <a class="btn btn-secondary" href="./#docs">All documentation</a>
      <a class="btn btn-secondary" href="{repo}/blob/main/docs/{source}">Edit this page on GitHub</a>
    </div>
  </div>
</main>

<footer class="site">
  <div class="wrap">
    <p style="margin:0">
      Copyright (c) 2024-2026 Rocky Stack. Released under the MIT License.
      <a href="{repo}">Source on GitHub</a>.
    </p>
  </div>
</footer>

</body>
</html>
"""


def esc(s):
    """Escape for an HTML attribute value."""
    return (s.replace("&", "&amp;").replace('"', "&quot;")
             .replace("<", "&lt;").replace(">", "&gt;"))


def render(source):
    """Render one markdown file to the full HTML page text."""
    path = os.path.join(HERE, source)
    with io.open(path, "r", encoding="utf-8") as f:
        text = f.read()

    kind, description = PAGES[source]

    heading = text.splitlines()[0].lstrip("# ").strip()

    html = markdown.markdown(
        text,
        extensions=["tables", "fenced_code", "sane_lists", "attr_list"],
        output_format="html5",
    )

    # Links between docs point at .md in the repo; on the site they are .html.
    # Links up to the repository root (../CHANGELOG.md) stay pointing at GitHub,
    # because those files are not published to the site.
    html = re.sub(r'href="\.\./([^"]+)"', r'href="%s/blob/main/\1"' % REPO, html)
    html = re.sub(r'href="([A-Za-z0-9._-]+)\.md(#[^"]*)?"',
                  lambda m: 'href="%s.html%s"' % (m.group(1), m.group(2) or ""),
                  html)

    # Tables need the horizontal-scroll wrapper the stylesheet expects.
    html = html.replace("<table>", '<div class="tablewrap"><table>')
    html = html.replace("</table>", "</table></div>")

    body = "\n".join("    " + line for line in html.splitlines())

    return TEMPLATE.format(
        title=esc(heading + " - STX.1 System Monitor"),
        heading=esc(heading),
        description=esc(description),
        canonical=SITE + source[:-3] + ".html",
        site=SITE,
        repo=REPO,
        kind=esc(kind),
        source=source,
        body=body,
    )


def main():
    ap = argparse.ArgumentParser(description="Build the STX.1 documentation pages.")
    ap.add_argument("--check", action="store_true",
                    help="verify generated pages are current; write nothing")
    args = ap.parse_args()

    stale = []
    for source in sorted(PAGES):
        if not os.path.exists(os.path.join(HERE, source)):
            sys.exit("FAIL: missing source %s" % source)

        target = os.path.join(HERE, source[:-3] + ".html")
        fresh = render(source)

        if args.check:
            if not os.path.exists(target):
                stale.append(source[:-3] + ".html (missing)")
                continue
            with io.open(target, "r", encoding="utf-8") as f:
                if f.read() != fresh:
                    stale.append(source[:-3] + ".html (out of date)")
            continue

        with io.open(target, "w", encoding="utf-8", newline="") as f:
            f.write(fresh)
        print("wrote %-44s %6d bytes" % (os.path.basename(target), len(fresh)))

    if args.check:
        if stale:
            print("STALE, run 'python docs/build-docs.py':")
            for s in stale:
                print("  " + s)
            sys.exit(1)
        print("All %d generated pages are current." % len(PAGES))
    else:
        print("\n%d pages generated from markdown." % len(PAGES))


if __name__ == "__main__":
    main()
