using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models.Sound {
   /// <summary>What one MIDI channel plays: its program, how loud the file sets it, and how many notes it has.</summary>
   public class MidiChannelInfo {
      public int Channel { get; init; }              // 0-15
      public bool IsDrums => Channel == 9;
      public int Program { get; init; } = -1;        // the first program the channel selects (-1: none, which plays program 0)
      public IReadOnlyList<int> Programs { get; init; } = Array.Empty<int>(); // every program it selects
      public int Notes { get; init; }
      public int Volume { get; init; } = 127;        // the typical controller 7 value (127 when the file never sets it, as mid2agb assumes)
      public int Expression { get; init; } = 127;    // the typical controller 11 value
      public double AverageVelocity { get; init; }
      public int LowestNote { get; init; }
      public int HighestNote { get; init; }
      /// <summary>What the game would hear from this channel: volume x expression, on the 0-127 scale, before any master volume.</summary>
      public double EffectiveVolume => Volume * Expression / 127.0;
   }

   /// <summary>A quick read of a MIDI file for the Sound tab: the channels and what they play, the loop markers, and how loud the file gets.</summary>
   public class MidiSummary {
      public int Format { get; init; }
      public int TicksPerBeat { get; init; }
      public IReadOnlyList<MidiChannelInfo> Channels { get; init; } = Array.Empty<MidiChannelInfo>();
      /// <summary>The time (in ticks) of the '[' and ']' markers mid2agb loops between, or -1.</summary>
      public int LoopStart { get; init; } = -1;
      public int LoopEnd { get; init; } = -1;
      public int LastNoteEnd { get; init; }
      /// <summary>
      /// The loudest moment: the sum over the notes sounding at once of volume x expression x velocity (each 0-1). A song of the games peaks at about 3 after its master volume;
      /// a file mixed for a soundcard at 7 or more clips on the GBA and sounds torn.
      /// </summary>
      public double PeakLoudness { get; init; }

      public IEnumerable<MidiChannelInfo> PlayingChannels => Channels.Where(channel => channel.Notes > 0);

      /// <summary>Reads the file. Throws MidiConversionException on a file that is not a MIDI file.</summary>
      public static MidiSummary Read(byte[] data) => Read(data, null);

      /// <summary>Reads the file; 'volumeScale' (per channel, 0-15) and 'foldExpression' say how the channels will be shaped, so PeakLoudness is the loudness after shaping.</summary>
      public static MidiSummary Read(byte[] data, double[] volumeScale, bool foldExpression = true) {
         if (data == null || data.Length < 14 || data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd') throw new MidiConversionException("This is not a MIDI file (it does not start with MThd).");
         int format = (data[8] << 8) | data[9], trackCount = (data[10] << 8) | data[11], division = (data[12] << 8) | data[13];
         var events = new List<(int time, int kind, int channel, int a, int b)>();
         int loopStart = -1, loopEnd = -1;
         int position = 14;
         for (int track = 0; track < trackCount && position + 8 <= data.Length; track++) {
            if (data[position] != 'M' || data[position + 1] != 'T' || data[position + 2] != 'r' || data[position + 3] != 'k') break;
            int length = (data[position + 4] << 24) | (data[position + 5] << 16) | (data[position + 6] << 8) | data[position + 7];
            int p = position + 8, end = Math.Min(data.Length, p + length);
            int status = 0, time = 0;
            while (p < end) {
               time += ReadVlq(data, ref p, end);
               if (p >= end) break;
               int b = data[p];
               if (b == 0xFF) {
                  if (p + 2 > end) break;
                  int type = data[p + 1]; p += 2;
                  int metaLength = ReadVlq(data, ref p, end);
                  if (type == 6 && metaLength >= 1 && p < end) {
                     var text = System.Text.Encoding.ASCII.GetString(data, p, Math.Min(metaLength, end - p)).Trim();
                     if (text == "[" && loopStart < 0) loopStart = time;
                     if (text == "]" && loopEnd < 0) loopEnd = time;
                  }
                  p += metaLength;
                  continue;
               }
               if (b == 0xF0 || b == 0xF7) { p++; int sysex = ReadVlq(data, ref p, end); p += sysex; continue; }
               if ((b & 0x80) != 0) { status = b; p++; }
               int kind = status & 0xF0, channel = status & 0x0F;
               if (kind == 0x80 || kind == 0x90 || kind == 0xA0 || kind == 0xB0 || kind == 0xE0) {
                  if (p + 2 > end) break;
                  int a = data[p], c = data[p + 1]; p += 2;
                  if (kind == 0x90 && c == 0) kind = 0x80;
                  events.Add((time, kind, channel, a, c));
               } else if (kind == 0xC0 || kind == 0xD0) {
                  if (p + 1 > end) break;
                  events.Add((time, kind, channel, data[p], 0)); p++;
               } else {
                  break; // not a channel event: the rest of this track can't be read
               }
            }
            position = end;
         }

         var channels = new List<MidiChannelInfo>();
         for (int channel = 0; channel < 16; channel++) {
            var mine = events.Where(e => e.channel == channel).ToList();
            var programs = mine.Where(e => e.kind == 0xC0).Select(e => e.a).ToList();
            var volumes = mine.Where(e => e.kind == 0xB0 && e.a == 7).Select(e => e.b).ToList();
            var expressions = mine.Where(e => e.kind == 0xB0 && e.a == 11).Select(e => e.b).ToList();
            var notes = mine.Where(e => e.kind == 0x90).ToList();
            channels.Add(new MidiChannelInfo {
               Channel = channel,
               Program = programs.Count > 0 ? programs[0] : -1,
               Programs = programs.Distinct().ToList(),
               Notes = notes.Count,
               Volume = volumes.Count > 0 ? Median(volumes) : 127,
               Expression = expressions.Count > 0 ? Median(expressions) : 127,
               AverageVelocity = notes.Count > 0 ? notes.Average(e => e.b) : 0,
               LowestNote = notes.Count > 0 ? notes.Min(e => e.a) : 0,
               HighestNote = notes.Count > 0 ? notes.Max(e => e.a) : 0,
            });
         }

         // the loudest moment, with the shaping applied
         var ordered = events.OrderBy(e => e.time).ToList();
         var volume = Enumerable.Repeat(127.0, 16).ToArray();
         var expression = Enumerable.Repeat(127.0, 16).ToArray();
         var active = new Dictionary<(int channel, int note), double>();
         double peak = 0;
         int lastNoteEnd = 0;
         foreach (var e in ordered) {
            if (e.kind == 0xB0 && e.a == 7) volume[e.channel] = e.b;
            else if (e.kind == 0xB0 && e.a == 11) expression[e.channel] = e.b;
            else if (e.kind == 0x90) {
               var scale = volumeScale != null && e.channel < volumeScale.Length ? volumeScale[e.channel] : 1.0;
               var level = Math.Min(127.0, volume[e.channel] * scale * (foldExpression ? expression[e.channel] / 127.0 : 1.0)) / 127.0;
               active[(e.channel, e.a)] = level * e.b / 127.0;
               peak = Math.Max(peak, active.Values.Sum());
            } else if (e.kind == 0x80) {
               active.Remove((e.channel, e.a));
               lastNoteEnd = Math.Max(lastNoteEnd, e.time);
            }
         }
         return new MidiSummary { Format = format, TicksPerBeat = division, Channels = channels, LoopStart = loopStart, LoopEnd = loopEnd, PeakLoudness = peak, LastNoteEnd = lastNoteEnd };
      }

      private static int Median(List<int> values) {
         var sorted = values.OrderBy(value => value).ToList();
         return sorted[sorted.Count / 2];
      }

      private static int ReadVlq(byte[] data, ref int p, int end) {
         int value = 0;
         for (int i = 0; i < 4 && p < end; i++) {
            int b = data[p++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) break;
         }
         return value;
      }
   }

   /// <summary>
   /// Brings the volumes of a MIDI file to the levels of the games' own music.
   ///
   /// MIDI files from the internet are mixed for a soundcard: every channel near 127, which the GBA's 8-bit mixer cannot add up without clipping, and the result is torn and glitchy.
   /// The games' songs sit much lower (they were converted with master volumes of 80 to 110, and their channels at 40 to 80). The numbers here come from the 209 songs of
   /// Pokémon Emerald: for each General MIDI program, the volume its channels typically play at (controller 7 times the song's master volume), and the loudest moment of a song.
   /// </summary>
   public static class MidiVolumeBalance {
      /// <summary>The typical channel volume (0-127, master volume included) of a program in the games' songs. Programs that no song plays enough to tell use the overall value.</summary>
      private static readonly Dictionary<int, int> typicalVolume = new() {
         [0] = 75, [1] = 60, [2] = 63, [3] = 45, [4] = 36, [5] = 51, [7] = 80, [8] = 80, [10] = 65, [13] = 80, [14] = 45, [17] = 56, [19] = 43, [21] = 31, [24] = 56, [29] = 47,
         [33] = 80, [35] = 80, [36] = 80, [38] = 79, [45] = 61, [46] = 65, [47] = 80, [48] = 51, [53] = 49, [56] = 65, [58] = 67, [60] = 59, [73] = 54, [74] = 60, [75] = 46,
         [80] = 39, [81] = 45, [82] = 58, [83] = 45, [84] = 63, [85] = 68, [86] = 42, [87] = 57, [88] = 90, [90] = 85, [92] = 34, [126] = 43, [127] = 42,
      };
      public const int TypicalVolume = 57;
      public const int TypicalDrumVolume = 45;
      /// <summary>The loudest moment of a typical song of the games (see MidiSummary.PeakLoudness), after its master volume.</summary>
      public const double TypicalPeak = 3.0;
      /// <summary>How far a channel is moved toward the games' level: 1 would set every channel to the games' volume whatever the file says, 0 would keep the file's mix. The file's own balance is kept in part.</summary>
      public const double Pull = 0.75;
      public const double MinScale = 0.3, MaxScale = 1.6;
      public const int MinMasterVolume = 40, MaxMasterVolume = 127;

      public static int VolumeFor(int program, bool drums) => drums ? TypicalDrumVolume : typicalVolume.TryGetValue(program, out var volume) ? volume : TypicalVolume;

      /// <summary>The factor for each channel (index 0-15) that brings its volume toward the games' level for its program. Channels without notes get 1.</summary>
      public static double[] ChannelScales(MidiSummary summary) {
         var scales = Enumerable.Repeat(1.0, 16).ToArray();
         foreach (var channel in summary.PlayingChannels) {
            var current = Math.Max(1.0, channel.EffectiveVolume);
            var target = VolumeFor(Math.Max(0, channel.Program), channel.IsDrums);
            var ratio = target / current;
            var scale = Math.Pow(ratio, Pull);
            scales[channel.Channel] = Math.Max(MinScale, Math.Min(MaxScale, scale));
         }
         return scales;
      }

      /// <summary>
      /// The master volume (mid2agb's -V, 0-127) that brings the loudest moment of the shaped file to the games' level, so nothing clips.
      /// 'peak' is the loudness after the channel scales (MidiSummary.Read with the scales).
      /// </summary>
      public static int MasterVolume(double peak) {
         if (peak <= 0) return 90;
         var volume = (int)Math.Round(TypicalPeak / peak * 127);
         return Math.Max(MinMasterVolume, Math.Min(MaxMasterVolume, volume));
      }
   }
}
