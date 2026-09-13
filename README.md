</p>

<h1 align="center">pk3DS </h1>

<br />

pk3DS is a ROM editor for all 3DS Pokémon games that utilizes a variety of tools developed by a large group of contributors. pk3DS was created 
using C# and primarily focuses on its randomizer to provide users with a fresh and new experience in the beloved Pokémon games. 

## Features tool for Pokemon X, Y (Stable) and ORAS (Experimental)

The editor features a vast variety of randomizers to make every run as unique as possible. The Randomizers currently available are:

- Trainer Battles (Pokemon / Items / Moves / Abilities / Difficulty / Classes)
- Wild Encounters (Species, Level, Gen/Legend Specific, ORAS DexNav won't crash!)
- Personal Data (Pokemon Types / Stats / Abilities / TM Learnset)
- Move Randomizer (Type / Damage Category)
- Move Learnset (Level Up / Egg Move)
- Evolutions
- TM Moves
- Special Mart Inventory
- Automatic repointing off Shops. (possibility off up too 300+ shop items)
- Ability to add subforms 1.4 only XY only
- Ability to add Items 1.4 only XY only

## Notes 1.4 XY only
- New subforms have no model of their own. They use the base form's model and name
  in game. only the data (stats, types, abilities, learnset) is separate.
- Start each ROM Expander run from a **clean RomFS**. Expanding a ROM that is already
  half-expanded makes the text lists mismatch, and those slots get skipped on purpose.
- Item names can be renamed in the Text Editor (Game Text, sub-file 98). Keep the
  trailing `[VAR ...]` on the line — it is the pluralisation variable, and removing it
  crashes the game when the shop draws the name.

## Installation and Usage

To begin using HKs-pk3DS you must first download the zip file. Once you've downloaded the zip file for the editor, dump your ROM from the 3DS Pokémon game of your choosing.

Run the pk3DS.WinForms.exe in Administrator mode right click the program and click "run as Administrator".
Once you open up the executable you can begin having fun with our editor and randomizing all the attributes and characteristics of the game to your liking.
Make sure X,Y and ORAS are on the original version any update installed may crash the games.
In the misc tool section you can now expend your ROM for adding subforms and items. DO NOT edit the name off an item in that list leave as is.

Owse.cs was alterred too be a functional tool for dumping bytes now doesnt crash and instead skips corrupted data.
Unk was reporpused too dump all scripts.
check_shop.py is there too look if any shops are missaligned on a hex level. (1. this shouldnt be a problem anymore 2. if its missaligned its likly not going too cause a crash anymore)

Below are some images of how the editor should look when you run it.

![RomFS Editing Tools](https://i.imgur.com/IDVCMfx.png)
![ExeFS Editing Tools](https://i.imgur.com/Ied0sVV.png)
![CRO Editing Tools](https://i.imgur.com/lUSGbw5.png)
<img width="373" height="469" alt="image" src="https://github.com/user-attachments/assets/86a42e13-263f-4165-9492-fa7a739afa7c" />
<img width="376" height="465" alt="image" src="https://github.com/user-attachments/assets/cba20590-44b6-4246-b7c8-bda8b55ff717" />
<img width="345" height="446" alt="items" src="https://github.com/user-attachments/assets/c15f1255-c1ec-433f-a671-f6eb841d0d0d" />
<img width="472" height="276" alt="tool" src="https://github.com/user-attachments/assets/d0966d50-cf80-40ff-8cb6-29adc43a52ad" />
<img width="521" height="460" alt="tool2" src="https://github.com/user-attachments/assets/d9e300bb-5467-4f23-a155-29e200429987" />






