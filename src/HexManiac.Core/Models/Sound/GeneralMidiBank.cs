using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models.Sound {
   /// <summary>
   /// The instruments a ROM can play a General MIDI file with, one for each of the 128 programs, gathered from every voicegroup of the ROM.
   ///
   /// The voicegroups of the GBA Pokémon games are laid out like General MIDI: slot 0 is the drum kit, slot 1 the piano, 56 the trumpet, 73 the flute...
   /// (the songs were written as MIDI files with those programs). Each voicegroup only fills the slots its song uses, and leaves the games' blank instrument
   /// in the others; the "All Instruments" patch is a voicegroup that fills every slot. So, with or without that patch, the instrument for a program is found
   /// by looking at that slot of every voicegroup: when the ROM has the patch its voicegroup wins, otherwise the definition that most voicegroups agree on
   /// (the same sample with the same envelope, which is how a sample is reused from song to song). A program no voicegroup fills is not in the ROM at all.
   ///
   /// The numbers are the ones in the file: a MIDI file stores programs 0-127 while sequencers show them as 1-128 ("1 Acoustic Grand Piano" is program 0),
   /// and mid2agb selects slot 0 for program 0. Channel 10 is drums: its program means a drum kit, which is slot 0 in the games.
   /// </summary>
   public class GeneralMidiBank {
      public const int Programs = VoicegroupCatalog.MaxEntries;
      public const int DrumSlot = 0;

      private readonly byte[][] tones = new byte[Programs][];
      private readonly string[] sources = new string[Programs];
      private readonly int[] votes = new int[Programs];
      private byte[] drumKit;
      private string drumSource;

      /// <summary>Where the instruments came from: the All Instruments voicegroup, or the voicegroups of the ROM.</summary>
      public bool FromAllInstruments { get; private set; }

      /// <summary>How many voicegroups were looked at.</summary>
      public int VoicegroupsScanned { get; private set; }

      /// <summary>The 12 bytes of the instrument for a program, or null when no voicegroup of the ROM has one.</summary>
      public byte[] Tone(int program) => program >= 0 && program < Programs ? tones[program] : null;

      public bool Has(int program) => Tone(program) != null;

      /// <summary>The drum kit for channel 10 (the games keep it in slot 0 of their voicegroups), or null.</summary>
      public byte[] DrumKit => drumKit;

      public bool HasDrumKit => drumKit != null;

      /// <summary>The name of the voicegroup the drum kit was taken from, or null.</summary>
      public string DrumSource => drumSource;

      /// <summary>The name of the voicegroup the instrument of a program was taken from, or null.</summary>
      public string Source(int program) => program >= 0 && program < Programs ? sources[program] : null;

      /// <summary>How many voicegroups define the chosen instrument of a program.</summary>
      public int Votes(int program) => program >= 0 && program < Programs ? votes[program] : 0;

      public int Count => tones.Count(tone => tone != null);

      private static readonly string[] programNames = "Acoustic Grand Piano|Bright Acoustic Piano|Electric Grand Piano|Honky-tonk Piano|Electric Piano 1|Electric Piano 2|Harpsichord|Clavinet|Celesta|Glockenspiel|Music Box|Vibraphone|Marimba|Xylophone|Tubular Bells|Dulcimer|Drawbar Organ|Percussive Organ|Rock Organ|Church Organ|Reed Organ|Accordion|Harmonica|Tango Accordion|Acoustic Guitar (nylon)|Acoustic Guitar (steel)|Electric Guitar (jazz)|Electric Guitar (clean)|Electric Guitar (muted)|Overdriven Guitar|Distortion Guitar|Guitar Harmonics|Acoustic Bass|Electric Bass (finger)|Electric Bass (pick)|Fretless Bass|Slap Bass 1|Slap Bass 2|Synth Bass 1|Synth Bass 2|Violin|Viola|Cello|Contrabass|Tremolo Strings|Pizzicato Strings|Orchestral Harp|Timpani|String Ensemble 1|String Ensemble 2|Synth Strings 1|Synth Strings 2|Choir Aahs|Voice Oohs|Synth Voice|Orchestra Hit|Trumpet|Trombone|Tuba|Muted Trumpet|French Horn|Brass Section|Synth Brass 1|Synth Brass 2|Soprano Sax|Alto Sax|Tenor Sax|Baritone Sax|Oboe|English Horn|Bassoon|Clarinet|Piccolo|Flute|Recorder|Pan Flute|Blown Bottle|Shakuhachi|Whistle|Ocarina|Lead 1 (square)|Lead 2 (sawtooth)|Lead 3 (calliope)|Lead 4 (chiff)|Lead 5 (charang)|Lead 6 (voice)|Lead 7 (fifths)|Lead 8 (bass + lead)|Pad 1 (new age)|Pad 2 (warm)|Pad 3 (polysynth)|Pad 4 (choir)|Pad 5 (bowed)|Pad 6 (metallic)|Pad 7 (halo)|Pad 8 (sweep)|FX 1 (rain)|FX 2 (soundtrack)|FX 3 (crystal)|FX 4 (atmosphere)|FX 5 (brightness)|FX 6 (goblins)|FX 7 (echoes)|FX 8 (sci-fi)|Sitar|Banjo|Shamisen|Koto|Kalimba|Bag pipe|Fiddle|Shanai|Tinkle Bell|Agogo|Steel Drums|Woodblock|Taiko Drum|Melodic Tom|Synth Drum|Reverse Cymbal|Guitar Fret Noise|Breath Noise|Seashore|Bird Tweet|Telephone Ring|Helicopter|Applause|Gunshot".Split('|');

      /// <summary>"1 Acoustic Grand Piano" for program 0: the number sequencers show (1-128) and the General MIDI name.</summary>
      public static string ProgramName(int program) => program >= 0 && program < Programs ? $"{program + 1} {programNames[program]}" : program.ToString();

      public IEnumerable<int> MissingPrograms => Enumerable.Range(0, Programs).Where(program => tones[program] == null);

      /// <summary>Looks at every voicegroup and keeps the best instrument of each slot.</summary>
      public static GeneralMidiBank Build(IDataModel model, IEnumerable<VoicegroupInfo> voicegroups, AllInstrumentsStatus allInstruments = null) {
         var bank = new GeneralMidiBank();
         var filler = VoicegroupCatalog.FillerTone();
         var candidates = new Dictionary<int, Dictionary<string, (byte[] bytes, int count, string source, ToneKind kind)>>();
         var drumCandidates = new Dictionary<string, (byte[] bytes, int count, string source, ToneKind kind)>();
         var list = (voicegroups ?? Enumerable.Empty<VoicegroupInfo>()).Where(info => info != null && !info.IsCustom && info.Address >= 0).ToList();
         bank.VoicegroupsScanned = list.Count;

         if (allInstruments != null && allInstruments.IsApplied && allInstruments.Address >= 0) {
            bank.FromAllInstruments = true;
            var source = allInstruments.AnchorName != null ? AllInstrumentsPatch.VoicegroupName : $"the voicegroup at {allInstruments.Address:X6}";
            var count = Math.Min(Programs, allInstruments.InstrumentCount);
            for (int slot = 0; slot < count; slot++) {
               var address = allInstruments.Address + slot * VoicegroupCatalog.EntrySize;
               var bytes = Read(model, address);
               if (bytes == null || IsFiller(bytes, filler) || !ToneData.TryRead(model, address, out var tone)) continue;
               if (tone.Kind == ToneKind.DrumKit) {
                  if (bank.drumKit == null) { bank.drumKit = bytes; bank.drumSource = source; } // the patch's first drum kit (slot 0 when it has one there)
                  continue;
               }
               bank.tones[slot] = bytes;
               bank.sources[slot] = source;
               bank.votes[slot] = 1;
            }
         }

         foreach (var info in list) {
            var entries = Math.Min(Programs, Math.Max(info.EntryCount, info.CopyCount));
            for (int slot = 0; slot < entries; slot++) {
               var address = info.Address + slot * VoicegroupCatalog.EntrySize;
               var bytes = Read(model, address);
               if (bytes == null || IsFiller(bytes, filler) || !ToneData.TryRead(model, address, out var tone)) continue;
               var key = BitConverter.ToString(bytes);
               if (tone.Kind == ToneKind.DrumKit) {
                  // the games keep their drum kit in slot 0; a drum kit anywhere else is still a drum kit
                  if (bank.FromAllInstruments && bank.drumKit != null) continue;
                  if (drumCandidates.TryGetValue(key, out var known)) drumCandidates[key] = (known.bytes, known.count + (slot == DrumSlot ? 1 : 0), known.source ?? info.Name, known.kind);
                  else drumCandidates[key] = (bytes, slot == DrumSlot ? 1 : 0, info.Name, tone.Kind);
                  continue;
               }
               if (bank.tones[slot] != null && bank.FromAllInstruments) continue; // the patch decides
               if (!candidates.TryGetValue(slot, out var bySlot)) candidates[slot] = bySlot = new Dictionary<string, (byte[], int, string, ToneKind)>();
               if (bySlot.TryGetValue(key, out var existing)) bySlot[key] = (existing.bytes, existing.count + 1, existing.source ?? info.Name, existing.kind);
               else bySlot[key] = (bytes, 1, info.Name, tone.Kind);
            }
         }

         foreach (var (slot, bySlot) in candidates) {
            if (bank.tones[slot] != null) continue;
            var best = Best(bySlot.Values);
            bank.tones[slot] = best.bytes;
            bank.sources[slot] = best.source;
            bank.votes[slot] = best.count;
         }
         if (bank.drumKit == null && drumCandidates.Count > 0) {
            var best = Best(drumCandidates.Values);
            bank.drumKit = best.bytes;
            bank.drumSource = best.source;
         }
         // General MIDI's program 0 is a piano, but the games keep their drum kit in slot 0 and the piano in slot 1: a melodic channel that plays program 0 gets the piano
         if (bank.tones[0] == null && bank.tones[1] != null) { bank.tones[0] = bank.tones[1]; bank.sources[0] = bank.sources[1]; bank.votes[0] = bank.votes[1]; }
         return bank;
      }

      /// <summary>The definition most voicegroups share; among equals a key split (an instrument sampled at several pitches) over a single sample, then the first by name (the game's own, before anything added later).</summary>
      private static (byte[] bytes, int count, string source, ToneKind kind) Best(IEnumerable<(byte[] bytes, int count, string source, ToneKind kind)> candidates) {
         return candidates
            .OrderByDescending(candidate => candidate.count)
            .ThenByDescending(candidate => candidate.kind == ToneKind.Keysplit || candidate.kind == ToneKind.DrumKit ? 2 : candidate.kind == ToneKind.DirectSound ? 1 : 0)
            .ThenBy(candidate => candidate.source ?? "~")
            .First();
      }

      private static byte[] Read(IDataModel model, int address) {
         if (address < 0 || address + VoicegroupCatalog.EntrySize > model.Count) return null;
         var bytes = new byte[VoicegroupCatalog.EntrySize];
         for (int i = 0; i < bytes.Length; i++) bytes[i] = model[address + i];
         return bytes;
      }

      private static bool IsFiller(byte[] bytes, byte[] filler) {
         for (int i = 0; i < filler.Length; i++) if (bytes[i] != filler[i]) return false;
         return true;
      }

      /// <summary>
      /// The slot the drum kit takes in a voicegroup made for these programs: slot 0, as in the games, unless a melodic channel plays program 0 (a piano in General MIDI), then the last slot.
      /// </summary>
      public static int DrumSlotFor(IEnumerable<int> programs) => programs.Contains(DrumSlot) ? Programs - 1 : DrumSlot;

      /// <summary>
      /// The 128 instruments of a new voicegroup for a song that plays these programs (and the drum kit in 'drumSlot' when it has drums; -1 for none): every other slot is the blank instrument.
      /// 'missing' gets the programs the ROM has no instrument for.
      /// </summary>
      public byte[] BuildVoicegroup(IEnumerable<int> programs, int drumSlot, List<int> missing = null) {
         var bytes = new byte[VoicegroupCatalog.Length];
         var filler = VoicegroupCatalog.FillerTone();
         for (int slot = 0; slot < Programs; slot++) Array.Copy(filler, 0, bytes, slot * VoicegroupCatalog.EntrySize, filler.Length);
         foreach (var program in programs.Distinct().OrderBy(program => program)) {
            if (program < 0 || program >= Programs || program == drumSlot) continue;
            var tone = tones[program];
            if (tone == null) { missing?.Add(program); continue; }
            Array.Copy(tone, 0, bytes, program * VoicegroupCatalog.EntrySize, tone.Length);
         }
         if (drumSlot >= 0 && drumSlot < Programs) {
            if (drumKit != null) Array.Copy(drumKit, 0, bytes, drumSlot * VoicegroupCatalog.EntrySize, drumKit.Length);
            else missing?.Add(drumSlot);
         }
         return bytes;
      }
   }
}
