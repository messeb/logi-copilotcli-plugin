#!/usr/bin/env python3
"""Adds the CopilotCLI folder buttons to every keypad profile.

Options+ shows an application's own profile whenever one exists, and only falls back to the
Default General Profile otherwise. So a button placed once is missing everywhere else, which reads
as "the plugin only works in some apps". This walks every profile on the keypad and adds whichever
of the three buttons it does not already have.

Quit Logi Options+ before running with --apply, or it may write the profiles back out from memory
and undo this. Run without --apply to see what it would do.
"""
import glob
import json
import os
import shutil
import sys
import uuid

ROOT = os.path.expanduser(
    "~/Library/Application Support/Logi/LogiPluginService/Applications/Loupedeck70")

PREFIX = "$CopilotCLI___#DynamicFolder___DynamicFolder#Loupedeck.CopilotCLIPlugin.Actions."

# Order matters: this is the order they are placed on free keys.
ACTIONS = [
    ("Active", PREFIX + "ActiveSessionsFolder"),
    ("Waiting", PREFIX + "WaitingSessionsFolder"),
    ("All", PREFIX + "AllSessionsFolder"),
]

CONTROL_TYPE = "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService"
PAGE_TYPE = "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutPage7, LoupedeckService"

KEYS_PER_PAGE = 9

apply = "--apply" in sys.argv


def control(control_id, action=None):
    return {"$type": CONTROL_TYPE, "controlId": control_id,
            "pressAction": action, "rotateAction": None}


def new_page(index):
    return {"$type": PAGE_TYPE,
            "name": uuid.uuid4().hex.upper(),
            "displayName": f"Page ({index + 1})",
            "description": None,
            "controls": [control(i) for i in range(KEYS_PER_PAGE)]}


def place(path):
    with open(path) as handle:
        profile = json.load(handle)

    changed = []
    touched = False

    for mode in profile.get("layout", {}).get("layoutModes", []) or []:
        for workspace in mode.get("workspaces", []) or []:
            pages = workspace.get("pressPages")
            if not pages:
                continue

            present = json.dumps(workspace)
            missing = [(label, a) for label, a in ACTIONS if a not in present]
            if not missing:
                changed.append("all three already present, left alone")
                continue

            for label, action in missing:
                # Prefer a free key on an existing page: a new page is one more press to reach.
                for page_index, page in enumerate(pages):
                    free = next((c for c in page["controls"] if not c.get("pressAction")), None)
                    if free is not None:
                        free["pressAction"] = action
                        changed.append(f"{label} -> page {page_index} key {free['controlId']}")
                        touched = True
                        break
                else:
                    page = new_page(len(pages))
                    page["controls"][0]["pressAction"] = action
                    pages.append(page)
                    changed.append(f"{label} -> new page {len(pages) - 1} (every page was full)")
                    touched = True

    if touched and apply:
        shutil.copy2(path, path + ".copilotcli.bak")
        tmp = path + ".tmp"
        with open(tmp, "w") as handle:
            json.dump(profile, handle, indent=2)
        os.replace(tmp, path)

    return changed


print("APPLY" if apply else "DRY RUN — nothing will be written")
for path in sorted(glob.glob(os.path.join(ROOT, "*", "Profiles", "*", "ProfileInfo.json"))):
    app = os.path.relpath(path, ROOT).split(os.sep)[0]
    with open(path) as handle:
        name = json.load(handle).get("displayName")
    for line in place(path) or ["no press pages found"]:
        print(f"  {app} ({name}): {line}")
