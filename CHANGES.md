# HKs-pk3DS — what this fork changes

Everything below is on top of upstream pk3DS. Nothing else is modified.

## Shops that can grow — `Subforms/Gen6/MartEditor6.cs`

Upstream edits shops in place, so a shop can never gain items. This fork relocates a
grown shop list into verified free space and repoints it.

- per-game pointer/count table addresses and free-space bounds (XY and ORAS)
- `ValidatePointerTables()` — if `code.bin` does not match the verified layout, it falls
  back to fixed-size editing and says so, rather than writing somewhere unsafe
- XY free space `0x4A5A44`–`0x4ADCBC`, confirmed all-zero in both X and Y
- ORAS support is **statically verified only** — the tool warns about this on open

## OWSE robustness — `Subforms/Gen6/Experimental/OWSE.cs`

Entity count/load calls are wrapped so a malformed zone logs a diagnostic instead of
throwing and taking the editor down. The NPC scan records skipped zones.

## ROM Expander — new

`Tools > Misc Tools > ROM Expander (Forms/Items)`.

- **Form slots**: grows `personal`, `levelup`, `evolution` and `megaevo` together — they
  share the form index, and growing one alone makes the game read past the end of the
  others. New records clone a template rather than being zeroed, because real hardware is
  far less tolerant of empty records than an emulator. The personal table is rebuilt as
  the trailing blob and kept last, which is where the game actually reads it from.
- **Auto-assignment**: optionally sets the template species' `FormStatsIndex` (`0x1C`) and
  `FormeCount` (`0x20`) so the new slot is reachable. Upstream never writes
  `FormStatsIndex` at all — its setter is `protected internal` and unused — so without
  this a new slot is an orphan no UI can wire up.
- **Items**: appends item records and extends every item text list, in **all** language
  slots. A slot only qualifies if its sub-file count matches the loaded archive's;
  without that guard the scan walks past the language range into unrelated archives.
- Reports **id ranges, never bare counts** (`Items: 719 (ids 0-718)`).

New files: `pk3DS.Core/Tools/RomExpander.cs`, `pk3DS.WinForms/Tools/Expander.cs`.

## Editor caps removed — `Subforms/Gen6/PersonalEditor6.cs`

Upstream hard-codes, in the XY branch:

```csharp
string[] temp_species = new string[799];
string[] temp_items   = new string[718];
```

The species dropdown was truncated to 799 entries regardless of the ROM, hiding every
appended form slot — and vanilla XY's own entries 799 and 800, plus the last item. Both
now size from the real data.

## Supporting changes

| file | change |
|---|---|
| `pk3DS.Core/CTR/GARC.cs` | `MemGARC.SetFilesResize()` — the `Files` setter throws on a changed file count; expansion needs to change it. The guard is kept so no editor resizes an archive by accident |
| `pk3DS.Core/Game/GARCFile.cs` | passthrough |
| `pk3DS.Core/Game/GameConfig.cs` | `GetGameTextIndex(TextName)` — public index lookup |
| `pk3DS.WinForms/Main.cs`, `Main.Designer.cs` | menu entry |

## Verified in game (Pokémon X)

- form slot: new entry set to Fighting/Fire with its own level-1 move, placed on Route 2
  as Bulbasaur Forme 1 — encountered and caught as expected
- item: appended, added to badge shop 8, bought in game
- item name: shown correctly on an **Italian** save, i.e. from a language slot other than
  the one pk3DS had loaded

## Building

```
dotnet build pk3DS.slnx -c Release -p:EnableWindowsTargeting=true
```

`-p:EnableWindowsTargeting=true` is required on Linux/macOS. Framework-dependent output
needs the Windows .NET Desktop Runtime; under Wine it must be installed **into the
prefix**. For a release that needs no runtime install, publish self-contained.
