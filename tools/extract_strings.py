#!/usr/bin/env python3
"""Lists user-facing English strings that need a Swedish translation (same rules as LocalizationTests)."""
import os, re, sys
root = sys.argv[1] if len(sys.argv) > 1 else "MuslimMatrimony.Web"
keys = set()
patterns = [
    re.compile(r'\bL\["((?:[^"\\]|\\.)*)"'),
    re.compile(r'\bl\["((?:[^"\\]|\\.)*)"'),
    re.compile(r'ErrorMessage = "((?:[^"\\]|\\.)*)"'),
]
user_facing = re.compile(r'"([A-Z][^"\\{}]*?(?:[.!?…])?)"')
for d, _, files in os.walk(root):
    if any(x in d for x in ("/bin", "/obj", "/Migrations", "wwwroot")):
        continue
    for f in files:
        path = os.path.join(d, f)
        if not (f.endswith(".cshtml") or f.endswith(".cs")):
            continue
        text = open(path, encoding="utf-8").read()
        for p in patterns:
            keys.update(m.group(1) for m in p.finditer(text))
        if f == "SharedResource.cs":
            keys.update(re.findall(r'=> "([^"]+)",', text))
        if "/Controllers/" in path or "/Areas/" in path:
            for line in text.splitlines():
                if "Status(" in line or "TempData[\"Status\"]" in line or re.search(r'=> "', line):
                    for m in user_facing.finditer(line):
                        s = m.group(1)
                        if " " in s and s[-1] in ".!":
                            keys.add(s)
for k in sorted(keys):
    print(k)
