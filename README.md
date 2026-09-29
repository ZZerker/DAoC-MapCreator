# MapCreator
Renders Dark Age of Camelot (DAoC) maps from the game client files: outdoor zones, capital cities and dungeons, with textured buildings, terrain relief and, in New Frontiers, the keeps and towers.

This is a fork of [Merec/DAoC-MapCreator](https://github.com/Merec/DAoC-MapCreator), which is no longer maintained. All credit for the original tool goes to Merec. This fork brings it up to date and renders the maps for the [TokaZerk UI](https://github.com/tokajer/TokaZerkUI) on the Eden freeshard. The UI can only show images, so everything a map needs has to be baked into it.

## Download
Get the latest build from [Releases](https://github.com/ZZerker/DAoC-MapCreator/releases). Unzip it anywhere and start `MapCreator.exe`. It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) and a DAoC client; the game folder is found on first start.

## What it renders
**Outdoor zones**
- Terrain textures with relief shading computed from the height map at full map size
- Rivers, lakes and lava; shallow water lets the ground show through, so shores fade into the water
- Trees drawn from their models with their own textures, leaves cut out by the texture's alpha
- Buildings and objects with their real textures, their baked lighting (dark maps) and a soft drop shadow; parts below the ground stay hidden like in the game
- Zone bounds

**New Frontiers keeps and towers**
- All 21 keeps and 54 towers as they stand on Eden, fully upgraded and undamaged, in their realm's textures
- Built piece by piece from the client's keep models and texture tables (`frontiers.mpk`)
- Optional, e.g. for maps that draw their own keep icons

**Capital cities** (Camelot, Jordheim, Tir na Nog)
- Built from the city models; ground layers (grass, soil, cobble, rock) blended per vertex as in the game

**Dungeons**
- Built from the placed rooms with their baked lighting, brightened so dark dungeons stay readable
- One extra map per level for the dungeons the client has level maps for (`zNNN_LL`, e.g. the six levels of Darkness Falls), the other levels shown faded underneath
- Instanced zones (`skycity` data, e.g. Underground Forest, Dream) included

City and dungeon maps use the same frame as the client and bestiary maps, so a position lines up at `(zone coordinate - offset) / width`. Each of them gets a `zNNN.frame.json` with its offset and width.

**Labels** (New Frontiers)
- Keep and tower names, bosses, places and docks, neighbor zone names along the map border
- Written as `zNNN.labels.json` next to each map and drawn at the final size by the DDS script, so the text stays sharp

## Changes in this fork
- Runs on .NET 10 (was .NET Framework 4.0) with Magick.NET 14 and `System.Numerics`
- Niflib built from source: [ZZerker/niflib.net](https://github.com/ZZerker/niflib.net), a .NET 10 fork without SharpDX, included as a git submodule
- Own triangle rasterizer with textures, trilinear filtering, 2x supersampling and a depth buffer, so every pixel shows the top surface
- Models use the texture layers and vertex lighting they name, not just their fallback texture
- All client zones are available; the ones missing in the curated list come from the client's `zones.dat`
- Model and texture replacements the client loads (`NIFPROXY.csv`, `TEXPROXY.csv`) are applied
- Command line batch mode for unattended rendering, several zones in parallel in one process, with a live console view; every option is also in the window
- Every model is drawn from its geometry; the hand-made images for relic temples, boats and piers are gone
- Scripts to convert the result to DDS for the UI and to render everything overnight
- Finds the game folder on first start (Eden and Blackthorn launcher, default install paths)
- Much faster: zones share their model and texture caches, and models are drawn by the own rasterizer instead of one ImageMagick draw call per triangle (fixtures of zone 171: 54 s before, under 5 s now). All 14 New Frontiers zones at 2048 px take about 2 minutes.
- `fixtures.xml` only holds overrides (texture mode, size limits, shadows), everything else is read from the models
- Code cleanup: naming rules enforced by `.editorconfig`, no build warnings

## Data sources
Everything comes from the game client, except:
- `data\MapFrames.csv`: city and dungeon map frames from the Eden bestiary
- `data\Keeps.csv`: New Frontiers keep and tower positions from the Eden war map, piece layouts from [Dawn-of-Light db-public](https://github.com/Dawn-of-Light/db-public)
- `data\Landmarks.csv`: places, docks and bosses for the labels, bosses from the Eden bestiary

## Roadmap
- [x] Zone list from the client's `zones.dat`, including Eden's own zones
- [x] Textured buildings instead of plain white shapes
- [x] City maps
- [x] Dungeon maps, one map per level where the client has level maps
- [x] City and dungeon maps in the same frame as the client maps
- [x] Renderer quality: relief shading, blended ground layers, vertex lighting, depth shaded water, shadows
- [x] New Frontiers keeps and towers
- [x] Names and points of interest on the New Frontiers maps
- [x] Maps for all playable dungeons, also the 16 that have no map in the game
- [x] Niflib on .NET 10, built from source
- [ ] Later: Old Frontiers keeps, re-render zones Eden has patched, replace the remaining .NET Framework libraries (MPKLib, tree view control)

## Requirements
- Windows x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A DAoC client installation
- For the DDS conversion: Python with Pillow, optionally `texconv.exe` from [DirectXTex](https://github.com/microsoft/DirectXTex/releases) in `tools\bin` for better DXT1 quality

## Build
Niflib is a git submodule, so clone with `--recursive` (or run `git submodule update --init` in an existing clone):
```
git clone --recursive https://github.com/ZZerker/DAoC-MapCreator.git
dotnet build MapCreator.sln -c Release
```
The program is written to `Releases\MapCreator.exe`. Pushing a tag `v*` builds and publishes a GitHub release.

## Usage
Start `MapCreator.exe`, select the zones and click create. Settings (map size, water, trees, keeps, bounds, output folder) are in the main window and the preferences.

Batch mode renders without user input and without the main window:
```
MapCreator.exe --render <zones> [--size 2048] [--dir nf_2048] [--log render.log] [--parallel 4] [--no-keeps] [--no-depth-water] [--labels-only] [--no-console]
```
A console window shows the run live: zones done, failed and left, the estimated time left, memory, one row per zone being rendered with its current step and progress, and the latest log lines. It stays open 30 seconds after the batch (or until a key is pressed), so unattended runs still end. `--no-console` runs without it. Closing the console window stops the render.

The log file is written as the render goes, one line at a time, so it keeps everything up to a crash. Every line carries its zone id. A zone that logged errors (for example a model that could not be drawn) ends with "Finished with N errors" and is counted in the summary. Other options come from the settings saved in the window; the exit code is 1 if a zone failed.

### Zone groups
`<zones>` is a comma separated list of zone ids and groups. The groups come from the same data as the zone selection in the window.

| Group | Examples |
|---|---|
| Everything | `all` |
| Realm | `alb`, `mid`, `hib` (or `albion`, `midgard`, `hibernia`) |
| Expansion | `nf`, `of`, `si`, `toa`, `cata`, `dr`, `lotm`, `classic`, `foundations`, `tutorial` (or the full name, e.g. `new-frontiers`) |
| Zone type | `outdoor`, `city`, `dungeon`, `instance`, `bg`, `indoor` |
| Client zones not in the curated list | `client` |
| Preset | the name of a preset saved in the zone selection |

`+` intersects groups, a comma adds them up. Case, spaces and dashes don't matter.

| Example | Renders |
|---|---|
| `--render nf+outdoor` | the 14 New Frontiers zones, without the battlegrounds |
| `--render alb+dungeon` | all Albion dungeons |
| `--render city` | Camelot, Jordheim, Tir na Nog |
| `--render 163,nf+bg` | zone 163 and the New Frontiers battlegrounds |
| `--render all` | every zone |

Unknown names are logged as a warning and skipped.

`tools\render_nf.ps1` renders all New Frontiers zones and converts them to DXT1 DDS files (`zNNN.dds`) with the labels drawn in:
```
.\tools\render_nf.ps1 -Size 2048 -Parallel 4 [-Zones 163,171] [-DdsSize 512]
```

`tools\render_all_shutdown.ps1` builds, renders every zone (or `-Zones`), converts New Frontiers to DDS and shuts the computer down (`-NoShutdown` to keep it running).

`tools\deploy_launcher_maps.ps1 -Source <render folder>` (run as administrator) copies the maps into the Eden launcher as `zoneNNN.jpg` and writes the city and dungeon frames into its `zones.json`, so the position marker lines up. The launcher's own files are saved once to `Output\launcher_backup_all`; `-Restore` puts them back.

### Please note
- Rendering is CPU and memory heavy, depending on the map size and the number of parallel zones.
- Use map sizes that are a power of 2: 512, 1024, 2048, 4096. The terrain textures have a native resolution of 4096 pixels.
- The first render converts the models and fills the cache (`data\polys8`, one file per model); later renders are faster. "Clear fixture polygon cache" in the Tools menu deletes it.

## Changelog
**2.2.0** (2026-09-29)
- Map labels drawn by MapCreator in the TokaZerk UI font (`--labels <dir> [--label-size N]`, `--labels-only` relabels existing renders): keeps, towers (GT, WT, OP, SP), neighbor zones, towns, channelers, dungeon entrances, bosses, ToA artifact encounters, and trainers and services in the capitals. Cities and dungeons are labeled in their frame. The point data files are not part of the release yet
- Battleground keeps and towers
- Walls built as bare vertical sheets (Avalon Isle's city wall) show their top edge
- Billboard trees that lose their leaves when seen from above are filled with their color
- Much faster model cache: one file per model, loaded outside the global lock (a full run went from 3.5 hours to 23 minutes)
- Trees are drawn with their textures instead of one average color
- Live console view for batch runs; the log is written directly and every line carries its zone id
- Errors are no longer swallowed: a model that cannot be drawn is logged with the reason, and the zone ends "Finished with N errors"
- Baked lighting for Darkness Falls, Veil Island and other dungeons that keep it in the detail slot
- Glows (additive meshes) add their light instead of being left out
- Dungeon brightness from the median, with a soft highlight curve instead of clipping (evenly lit dungeons are no longer washed out)
- Relic temples, boats, logs, piers and rock barriers are drawn from their models instead of old hand-made images
- City and dungeon maps write a frame file; new script to put the maps into the Eden launcher
- Tree clusters and trees only `fixtures.xml` knows get their tree color; a warning is logged when a model falls back to its category color
- Passage of Conflict entrances labeled on the Irish Sea maps
- Less memory: models and textures only one zone uses are released after it, image leaks closed. The model cache is saved safely, so an interrupted save no longer breaks it
- Fixes: seams between tiled ground models, the Agramon arena mountaintop, water proxies nested below their node (Galladoria), models missing in the client are skipped, battleground portal keep positions, overlapping lakes no longer fade each other (bog of zone 269), no bright circles around Mag Mell (Eden's Lough Derg copies), models with their own ground no longer show it as a square (wreck in zone 077), dungeon water only over its basin (zone 332), room maps win over the dark slot gradient, a `fixtures.xml` entry without category no longer stops every render, the bounds color field works
- The model cache moves to the `data\polys8` folder; an existing `polys8.mpk` is split into it once

**2.1.0** (2026-09-26)
- Baked lighting (dark maps) on buildings and city floors
- Zone groups in batch mode (`all`, realms, expansions, zone types), so every playable dungeon renders in one run
- Niflib built from source as a .NET 10 fork, SharpDX removed
- Zoomed area maps (`zNNN_AA.dds`) and PNG previews in the DDS script
- Fixes: no guessed fill for bound pieces inside the map, Dun Crimthain heading, old NIF 3.03 models fall back to their newer copy
- Trees are always drawn from their models; the prerendered tree images are gone

**2.0.0** (2026-09-26): first release of the fork, everything listed above.

## Original changelog (Merec)
**2017-10-01**
- Update to Magick.NET 7.0.7.300
- Update to SharpDX 4.0.1
- Replaced my Niflib.NET with https://github.com/dol-leodagan/niflib.net

**2015-08-23**
- Fixed crash when fixtures have wrong coordinates (thanks to HunabKu for reporting!)

**2014-02-16**
- bug fix in image replacement
- added image replacement examples for relic temples
- found some trees that are nor marked as trees (please report if you find more)
- changed all opacity to transparency fields to be more clear
- added option to set tree transparency per map
- reset settings, too many changes to convert them

**Older changes**
- added fixtures rendering
- added tree rendering
- full rewrite of the bound-calculation
- full rewrite of the water-calculation
- UI improvements
- fixed wrong calculations
- optimized default settings
- rewrote bounds generator, now there will be no more flood fills
- optimized river rendering
- add lava and a texture to water

Merec's thanks go to Schaf, Metty and Leodagan who helped a lot.

## License
GPL v2 or later, as the original.
