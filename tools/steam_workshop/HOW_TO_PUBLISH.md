# Publishing ZombiePlague to the Steam Workshop

This uses SteamCMD (Valve's official CLI tool) since the built-in launcher
uploader isn't available. SteamCMD is already downloaded to
`tools\steamcmd\steamcmd.exe`.

## One-time upload (creates a new Workshop item)

Open a terminal in this repo and run:

```powershell
tools\steamcmd\steamcmd.exe +login <your_steam_username> +workshop_build_item tools\steam_workshop\workshop_item.vdf +quit
```

- It will prompt for your **password**, and a **Steam Guard code** (sent to
  your email/phone) if this machine hasn't logged in via SteamCMD before.
  Type these directly into the terminal yourself - nobody else sees them.
- On success it prints a **Published File ID** - this is your Workshop
  item's permanent ID. Save it somewhere (e.g. paste it into this file).
- Visit `https://steamcommunity.com/sharedfiles/filedetails/?id=<that_id>`
  to see the listing, fix up the description (steamcmd doesn't render
  Workshop BBCode nicely - paste `description.txt`'s contents into the
  web editor instead of relying on the VDF's `description` field), add
  more screenshots, and set the final visibility (currently defaults to
  **Friends Only** - change to Public on the item's page when you're ready).

## Publishing an update later

Once you have a Published File ID, put it into `workshop_item.vdf`'s
`"publishedfileid"` field (replacing `"0"`) and re-run the same command -
it updates the existing item instead of creating a new one.

## Files in this folder

- `workshop_item.vdf` - the upload config (app id, content folder, preview
  image, visibility). Content folder points at `ZombiePlague\` directly -
  make sure `bin\Win64_Shipping_Client\ZombiePlague.dll` is built and
  up to date before uploading (see the main repo's build instructions).
- `description.txt` - a drafted Workshop description; edit freely.
- `..\workshop_preview.png` - a placeholder 512x512 preview image. Replace
  it with real artwork before going public - update `previewfile` in the
  VDF if you rename/move it.
