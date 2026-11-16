# CUBHMA 0.7.4

CUBHMA - Cradi's Unofficial Build of Hex Maniac Advance: a performance-tuned Hex Maniac Advance with extra editors for pokeemerald-expansion. Works best with Cradi's Ultimate Build of Emerald (CUBE).
Download the zip below, extract it anywhere, and run `HexManiacAdvance.exe`.

## What's new since 0.7.3

### Update CUBE
- **Tilesets and blocksets survive the update.** The blocks and attributes of a tileset were compared one byte deep, so blocks you drew were dropped and the new CUBE's blocks won, while your maps kept the block numbers you used: the tiles looked changed and the blockset "messed up". They are now carried over whole, like any other edit. The same for scripts that map events point at (only their first byte was compared). A map you pointed at another of CUBE's tilesets, which you then also edited, no longer gets that tileset written over the one the map used before (every map sharing it changed). Names you gave to data of your own (a script, a sprite) go with the data to its new place.
- The status says whether the in-game error screen could be turned off, and every entry into it is turned off (a build with two copies of the code was only half off).
- Run Update CUBE once on a ROM updated with 0.7.3 or earlier: it repairs what the old update dropped, as far as your `.toml` still describes it.

### Trainer editor
- **Format Guide** button next to Default Moves / Default Items: a window with how a party is written (level, species, IVs, item, moves, and the extras line: ability, nature, gender, shiny, ball, friendship, EVs, Tera type, Dynamax / Gigantamax / Dynamax level, Mega Evolution and Z-Moves through the held item), an example party, and what the game needs for each to work (which item the player needs, which flag: 0x1154 Tera Orb charged, 0x1155 Dynamax).

### Sound tab: MIDI files
- **The instruments of the ROM for a MIDI file.** A MIDI file's programs are General MIDI (57 Trumpet, 49 Strings, 48 Timpani...). The games' voicegroups are laid out the same way, each one filling only the slots its song uses, so the instrument for a program is now gathered from every voicegroup of the ROM (the definition most of them share wins; the All Instruments voicegroup wins when the ROM has it), and the song gets a voicegroup of its own holding exactly the instruments it plays. CUBE has an instrument for all 128 programs. A channel whose instrument the ROM does not have is left out, and the status line says which. The numbers are the file's own 0-127 (sequencers show 1-128: "1 Acoustic Grand Piano" is program 0, which the games keep in slot 1 behind the drum kit; a melodic channel on program 0 gets the piano, channel 10 gets the drum kit in slot 0).
- **Volumes like the game.** MIDI files are mixed for a soundcard and clip on the GBA, which is the torn, glitchy sound. Each channel is now brought toward the volume the games give its instrument (measured over the 209 songs of Emerald), expression (controller 11, which mid2agb drops) is folded into the volume, and the master volume is set so the loudest moment matches a song of the games. Both are checkboxes under "MIDI files", on by default; the old behaviour (convert for the chosen voicegroup, fixed master volume) is what you get with them off. .s files are not touched by any of this.
- **Track editor** (under the song's voicegroup): the song on a timeline, one lane per track with its notes against bars and beats, the loop as a shaded span with a draggable start and end handle (snap to bar, beat, 1/8, 1/16 or free; Shift ignores the snap; Ctrl + wheel zooms), or typed as bar:beat:tick. Works on every song, not only the ones made from MIDI files.
  - **Play from anywhere**: click in the tracks to put the cursor there and press "▶ From cursor" (or double-click, or Space) and the song plays from that exact spot; "From start" plays it from the beginning. While it plays, a playhead runs along the timeline and "playing at" shows the position; "Start here" / "End here" (or the [ and ] keys) put the loop markers where it is, on the snap grid.
  - **Per-track volume**: a slider next to each track shows the track's real volume (0-127, the song's own value to start with) and sets it; the track's swells and fades are scaled to start there. After writing, the slider stays where you put it, and a muted track (0) can be brought back up. Hear it with ▶ From cursor before writing.
  - **Remove a track**: the ✕ in front of a track leaves it out of the song (its lane fades; press again to keep it). The written song has fewer tracks.
  - **▶ Play follows the editor**: with a cursor placed or changes not written yet, the main ▶ Play button plays from the cursor with those changes.
  - A loop start alone is a loop to the end of the song (the Write button was greyed out until an end was set).
  - The track editor is open by default and sits under the song list in a column that scrolls: the list, the .s text and the editor are all there. The Pokémon cries column is narrower (2:3), and the splitter still moves it.
  - **Instrument per track**: a drop-down next to each track lists the ROM's own instruments (General MIDI numbering, 1 Acoustic Grand Piano ... 128, plus the drum kit). Choosing one replaces the track's instrument changes, and on writing the song gets its own copy of the voicegroup with that instrument in the right slot, so no other song that shares the voicegroup changes. The copy is named after the song and shows up in the voicegroup list.
  - "Write changes" writes the loop, the volumes and the instruments; "No loop" makes the song play once; "Revert" puts everything back. The song is written again in free space (the old bytes stay until you repoint or clear them); Undo takes it back.

## What was new in 0.7.3

### CUBE: no more blue error screen
- **Apply CUBE** and **Update CUBE** turn off the blue screen the game shows when one of its data checks fails ("invalid ability num 4 for species 25", "INVALID ITEM 4369"...). The game goes on quietly instead, so a randomized or hand-edited ROM plays. The few checks for failures the game can't survive (out of memory) stay. Run Update CUBE once on a ROM made with an earlier CUBHMA.

### Update CUBE
- A ROM that already is the current CUBE can be updated too: your changes are moved onto a fresh copy of CUBE and its `.toml` is rebuilt from the one that ships with the program (it used to say there was nothing to update). Use it to repair a `.toml` saved by an older CUBHMA.
- A map layout that you pointed at another tileset keeps it, and the tileset it used to have (shared with other maps) is no longer changed.

### Move editor
- **Extra effects of a move** (the chance of a burn, a flinch, a stat change...) are fields: effect (a list with the game's names), chance (0 = always), self and the other flags, the three argument bytes, and the stat stages, with Add / Remove buttons. Blizzard shows FREEZE at 10%, Tri Attack its three effects, Ancient Power every stat +1. The number of effects is written into the move's flags for the game. An older `.toml` gets the fields when the ROM is opened.
- The category showed Moonblast and other special moves as "status" and status moves as "-1" (the list was one entry short). Contest effect, contest category and combo starter use the expansion's own names, and a negative priority (Whirlwind at -6) shows as a negative number instead of 10 to 15. An older CUBE `.toml` is repaired when opened (only lists that are still the old defaults are replaced, so your own edits stay).
- A ROM edited with an older CUBHMA crashed the program when the moves table was drawn (a list of a bit field had a missing entry). A missing entry shows as its number now, and the window no longer goes down on a redraw.

### Item editor
- A TM or HM shows the **move it teaches** in a "Move taught" box, and changing it writes both lists the game really reads (CUBE: TM01-TM100 and HM01-HM08), so the change shows up in game. TM51-TM100 are editable too.
- One item whose name cannot be read (a placeholder item 0) no longer removes the names of all the other items from the lists.

### Pokemon editor
- The **evolutions** of a Pokemon (CUBE) are fields (method, argument, species, conditions) with Add and Remove buttons, like the evolutions of the old games. The item of an ITEM evolution and the item, species, move, type or nature of a condition show as names, not numbers. A list longer than 6 (Eevee) keeps the text editor.
- Faster again when jumping between Pokemon: form changes stay text, and fewer controls are built per Pokemon.
- The **type chart** (CUBE): every row is named after its attacking type and every field after its defending type. Old `.toml` files are repaired when opened.

### Overworld sprites
- The sprite data (palette ID, length, width, height, OAM, animations...) is shown as fields under the gallery and can be typed in right there: clicking a field no longer switched the table tool to the pointed-to struct.
- Hex fields (palette IDs, tags, flags...): type the digits with or without `0x` (1005 is 0x1005; it used to be read as decimal); text that is not a value changes nothing.

### Intro Manager
- Works on **Ruby and Sapphire** (all three US versions) next to Emerald, FireRed, LeafGreen and CUBE: the Pokemon of the speech, Birch's picture, the speech and title music.
- Works on **HUBOL** and other CFRU / Dynamic Pokemon Expansion FireRed bases.
- **New game spawn** on vanilla Emerald and Emerald-based hacks (also ones built with a newer compiler), and on FireRed and LeafGreen. It shows the real start tile instead of -1/-1, finds a start position that another tool changed, and the map list can no longer change the spawn by itself (it used to write a map such as Battle Frontier the moment the list was rebuilt; press "Back to the original" if your ROM was hit).

### Character Customization and trainers
- The buttons that edit the boy's and girl's front and back pictures open the image editor (they did nothing), and the back picture covers all four poses.
- Trainer editor (CUBE): the battle type is a Single / Double choice.

### Scripts
- `givePokemon` and the other commands that take a species find the names on CUBE again, and typing a name shows the list.
- Documentation of the CUBE flags and variables (`docs/CUBE_FLAGS.md`).

## What's new in CUBE (the matching ROM)
- **DexNav** is a key item with a DEXNAV line on the START menu under the Bag, and it no longer asks you to move slowly.
- **Overworld wild Pokemon** are on by default, and shinies show as shiny overworld Pokemon.
- **EXP All** is a key item you can toggle with the item or SELECT: party members that did not battle get 20% by default, and eggs never gain EXP.
- **Level caps** are off by default.
- **Randomizer menu** on New Game, with a seed. It does not break the Birch intro, and Birch's bag shows the randomized starters.
- **Reusable TMs** option, off by default.
- **New game spawn** (map, X/Y, truck) comes from a table you edit.
- **Debug menu**: Give items scrolls back to the top, and the debug NPC can give all Pokemon.
- **ROM size**: about 82.96% used (was 86.8%) after removing the unused All-Instruments sound bank.
- **Blue screen fixed**: a map script that moves or changes an object the map editor deleted now does nothing, like vanilla Emerald, instead of showing the error screen.
