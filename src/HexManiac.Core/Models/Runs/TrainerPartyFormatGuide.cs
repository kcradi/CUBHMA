namespace HavenSoft.HexManiac.Core.Models.Runs {
   /// <summary>
   /// The text that the trainer editor's "Format guide" button shows: how a trainer's party is written in the editor, with an example Pokémon,
   /// and what the game needs for each of the extras to work (CUBE / pokeemerald-expansion).
   /// </summary>
   public static class TrainerPartyFormatGuide {
      public const string Expansion = @"TRAINER PARTY FORMAT (CUBE)

One Pokémon is one block of lines; leave an empty line between Pokémon. Names with spaces go in quotes.

   LEVEL ""Species"" (IVs=31) @""Held Item""      <- level, species, IVs, held item (the @item is optional)
   - Move 1                                    <- up to four moves; leave them out to use the level-up moves
   - Move 2
   * key=value key=value ...                   <- the extras line: everything below is optional

EXAMPLE

   42 ""Charizard"" (IVs=31/0/31/31/31/31) @""Charizardite X""
   - Flare Blitz
   - Dragon Claw
   - Earthquake
   - Roost
   * ability=Blaze nature=Jolly gender=male shiny=yes ball=""Ultra Ball"" friendship=255 evs=0/252/4/252/0/0 tera=Fire

   38 ""Gyarados"" (IVs=31) @""Leftovers""
   - Waterfall
   - Ice Fang
   * nature=Adamant ball=""Dive Ball"" dynamax=yes dmaxlevel=10

IVs and EVs are six numbers in the game's order: HP / Attack / Defense / Speed / Sp.Atk / Sp.Def.
A single number (IVs=31) means all six. EVs are 0 to 252 each, 510 in total; no 'evs=' means no EVs.

THE EXTRAS (the '*' line)

   ability=Name         The ability, which must be one of the species' own (an ability it can't have is ignored
                        and the game picks one). Leave it out for the species' first ability.
   nature=Name          Adamant, Jolly, Modest, ... Leave it out and the game chooses a random nature.
   gender=male|female   Leave it out for a random gender (species with one gender ignore it).
   shiny=yes            The Pokémon is shiny.
   ball=""Name""          The ball it was caught in (""Great Ball"", ""Premier Ball""...), ball=random for a random one.
                        Leave it out for the trainer class's ball (Poké Ball for most).
   friendship=N         0 to 255. Matters for Return / Frustration and some abilities.
   evs=a/b/c/d/e/f      Effort values in the order above.
   nickname=""Name""      Shown for information: change a nickname in the table itself (the nickname field).
   tags=HEX             Trainer mon tags, as a hex number (rarely needed).

   tera=Type            TERASTALLIZATION. The Pokémon Terastallizes into this type (Fire, Water, Stellar...)
                        the first chance it gets. Leave it out and it never Terastallizes, unless the trainer has
                        the 'smartTera' AI flag, which lets the AI decide (the type is then the Pokémon's own).
                        The PLAYER needs the Tera Orb in the bag and flag 0x1154 (FLAG_TERA_ORB_CHARGED) set;
                        flag 0x1153 (FLAG_TERA_ORB_NO_COST) keeps it charged forever.

   dynamax=yes          DYNAMAX. The Pokémon Dynamaxes the first chance it gets.
   gmax=yes             Gigantamax instead of Dynamax (only for species with a Gigantamax form; implies dynamax=yes).
   dmaxlevel=N          Dynamax level 0 to 10 (10 is the default): how much its HP grows.
                        The PLAYER needs the Dynamax Band in the bag and flag 0x1155 (FLAG_DYNAMAX_BATTLE) set.
                        A Pokémon holding a Mega Stone or a Z-Crystal does not Dynamax.

   MEGA EVOLUTION       No key: give the Pokémon its Mega Stone as the held item (@""Charizardite X"", @""Gardevoirite""...)
                        and the trainer Mega Evolves it. Rayquaza needs to know Dragon Ascent.
                        The PLAYER needs the Mega Ring in the bag.

   Z-MOVES              No key: give the Pokémon a Z-Crystal as the held item (@""Firium Z""...) and a move of that type.
                        The PLAYER needs the Z-Power Ring in the bag.

   A trainer uses only one of these per battle (one Mega, or one Tera, or one Dynamax): the first Pokémon that can.

AI

   The trainer's AI flags are in the trainer's own fields (checkBadMove, tryToFaint, checkViability, smartSwitching,
   omniscient, smartTera, ...). 'smartTera' lets the AI choose whether to Terastallize instead of always doing it.

SHOWDOWN / .party SETS

   You can also paste a Pokémon Showdown set or a pokeemerald-expansion .party set into the editor; it is converted:

   Charizard @ Charizardite X
   Level: 42
   Ability: Blaze
   EVs: 252 Atk / 4 Def / 252 Spe
   Jolly Nature
   Tera Type: Fire
   - Flare Blitz
   - Dragon Claw

'Default Moves' fills every Pokémon's moves with the last four level-up moves; 'Default Items' clears the held items.
Undo (Ctrl+Z) takes any change back.";

      public const string Vanilla = @"TRAINER PARTY FORMAT

One Pokémon is one block of lines; leave an empty line between Pokémon. Names with spaces go in quotes.

   LEVEL ""Species"" (IVs=31) @""Held Item""      <- level, species, IVs, held item
   - Move 1                                    <- up to four moves
   - Move 2

EXAMPLE

   42 ""Charizard"" (IVs=31) @""Leftovers""
   - Flamethrower
   - Dragon Claw
   - Earthquake
   - Aerial Ace

   38 ""Gyarados"" (IVs=25)
   - Surf
   - Ice Beam

In the original games a trainer's Pokémon has one IV value for all six stats (0 to 31) and no EVs; its nature, gender
and ability come from a random personality. Whether the Pokémon can hold items or have custom moves depends on the
trainer's 'structType' field: 0 = neither, 1 = custom moves, 2 = held items, 3 = both (it is changed in the trainer's
fields; the editor adds the moves and items it can).

'Default Moves' fills every Pokémon's moves with the last four level-up moves; 'Default Items' clears the held items.
Undo (Ctrl+Z) takes any change back.";
   }
}
