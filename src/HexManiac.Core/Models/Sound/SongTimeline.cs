using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models.Sound {
   /// <summary>A note of a song on the timeline: when it starts (in ticks from the start of the song), how long it sounds, which key and how loud.</summary>
   public class TimelineNote {
      public int Track { get; init; }
      public int Start { get; init; }
      public int Length { get; init; }
      public int Key { get; init; }
      public int Velocity { get; init; }
      public int End => Start + Length;
   }

   /// <summary>
   /// A command of a track other than a note or a wait, with the bytes the engine reads for it (the command byte first, even when the song wrote it with running status).
   /// </summary>
   public class TimelineCommand {
      public int Track { get; init; }
      public int Time { get; init; }
      public byte[] Bytes { get; init; }
      public int Command => Bytes[0];
   }

   public class TimelineTrack {
      public int Index { get; init; }
      public List<TimelineNote> Notes { get; } = new();
      public List<TimelineCommand> Commands { get; } = new();
      /// <summary>The tick the track ends at (its FINE, or the GOTO that loops it).</summary>
      public int End { get; set; }
      /// <summary>The ticks of the track's own loop (the GOTO at its end and the place it jumps to), or -1.</summary>
      public int LoopStart { get; set; } = -1;
      public int LoopEnd { get; set; } = -1;
      /// <summary>The first instrument the track selects, or -1.</summary>
      public int Voice => Commands.FirstOrDefault(command => command.Command == 0xBD)?.Bytes[1] ?? -1;
      public int LowestKey => Notes.Count == 0 ? 60 : Notes.Min(note => note.Key);
      public int HighestKey => Notes.Count == 0 ? 60 : Notes.Max(note => note.Key);
   }

   /// <summary>
   /// A song laid out in time: every note of every track with its start tick, so the song can be drawn, and its loop (where it jumps back from and to).
   /// The ticks are the engine's: 24 to a quarter note, 96 to a whole note (what mid2agb writes with its default time base).
   /// Patterns (PATT) and repeats (REPT) are unrolled, so the timeline is what the player hears; a GOTO back to an earlier point of the track is the loop and ends the walk.
   /// </summary>
   public class SongTimeline {
      public const int TicksPerBeat = 24;
      public const int BeatsPerBar = 4;
      public const int TicksPerBar = TicksPerBeat * BeatsPerBar;

      public SongHeader Header { get; init; }
      public IReadOnlyList<TimelineTrack> Tracks { get; init; } = Array.Empty<TimelineTrack>();
      /// <summary>The first tempo of the song in beats per minute (the game's default is 150 when a song never sets one).</summary>
      public int Tempo { get; init; } = 150;
      /// <summary>The tick the longest track ends at.</summary>
      public int Length => Tracks.Count == 0 ? 0 : Tracks.Max(track => Math.Max(track.End, track.Notes.Count == 0 ? 0 : track.Notes.Max(note => note.End)));
      /// <summary>The loop of the song: where the tracks jump back to and from (the first track that loops decides; -1 when the song plays once).</summary>
      public int LoopStart { get; init; } = -1;
      public int LoopEnd { get; init; } = -1;
      public bool HasLoop => LoopStart >= 0 && LoopEnd > LoopStart;
      public IEnumerable<TimelineNote> AllNotes => Tracks.SelectMany(track => track.Notes);

      public double SecondsPerTick => 60.0 / Math.Max(1, Tempo) / TicksPerBeat;
      public string Describe(int tick) => $"{tick / TicksPerBar + 1}:{tick % TicksPerBar / TicksPerBeat + 1}:{tick % TicksPerBeat:D2}";

      /// <summary>Parses "bar:beat:tick" (1-based bar and beat), "bar:beat", or a plain tick count.</summary>
      public static bool TryParsePosition(string text, out int tick) {
         tick = 0;
         if (string.IsNullOrWhiteSpace(text)) return false;
         var parts = text.Trim().Split(':', '.');
         if (parts.Length == 1) return int.TryParse(parts[0], out tick) && tick >= 0;
         if (!int.TryParse(parts[0], out var bar) || bar < 1) return false;
         int beat = 1, rest = 0;
         if (parts.Length > 1 && (!int.TryParse(parts[1], out beat) || beat < 1 || beat > BeatsPerBar)) return false;
         if (parts.Length > 2 && (!int.TryParse(parts[2], out rest) || rest < 0 || rest >= TicksPerBeat)) return false;
         tick = (bar - 1) * TicksPerBar + (beat - 1) * TicksPerBeat + rest;
         return true;
      }

      /// <summary>Reads the song at a header. The walk is bounded, so a damaged song gives a short or empty timeline rather than an endless one.</summary>
      public static SongTimeline Read(IDataModel model, SongHeader header) {
         var tracks = new List<TimelineTrack>();
         int loopStart = -1, loopEnd = -1, tempo = 150;
         bool tempoSet = false;
         for (int i = 0; i < header.TrackCount; i++) {
            var track = new TimelineTrack { Index = i };
            ReadTrack(model, header.Tracks[i], track, out var firstTempo);
            if (!tempoSet && firstTempo > 0) { tempo = firstTempo; tempoSet = true; }
            if (loopStart < 0 && track.LoopStart >= 0 && track.LoopEnd > track.LoopStart) { loopStart = track.LoopStart; loopEnd = track.LoopEnd; }
            tracks.Add(track);
         }
         return new SongTimeline { Header = header, Tracks = tracks, Tempo = tempo, LoopStart = loopStart, LoopEnd = loopEnd };
      }

      private const int MaxEvents = 200000;

      private static void ReadTrack(IDataModel model, int start, TimelineTrack track, out int firstTempo) {
         firstTempo = 0;
         int pc = start, time = 0, runningStatus = 0, key = 60, velocity = 127;
         var patternStack = new Stack<int>();
         var repeats = new Dictionary<int, int>(); // REPT address -> times taken
         var ticksAt = new Dictionary<int, int>();  // the tick a command was first reached at (for the loop's start)
         var ties = new Dictionary<int, TimelineNote>(); // key -> the tie waiting for its EOT
         var tiesStart = new Dictionary<int, int>();
         int guard = MaxEvents;
         while (pc >= 0 && pc < model.Count && guard-- > 0) {
            if (patternStack.Count == 0 && !ticksAt.ContainsKey(pc)) ticksAt[pc] = time;
            int command = model[pc];
            int argStart;
            if (command < 0x80) {
               command = runningStatus;
               if (command < 0x80) { pc++; continue; }
               argStart = pc;
            } else {
               argStart = pc + 1;
               if (command >= 0xBD) runningStatus = command;
            }
            int Arg(int index) => argStart + index < model.Count ? model[argStart + index] : 0;
            bool HasArg(int index) => argStart + index < model.Count && model[argStart + index] < 0x80;

            if (command <= 0xB0) { time += MPlayDef.ClockTable[command - 0x80]; pc = argStart; continue; }
            if (command >= 0xCF) {
               int length = MPlayDef.ClockTable[command - 0xCF];
               int args = 0;
               if (HasArg(0)) { key = Arg(0); args = 1; if (HasArg(1)) { velocity = Arg(1); args = 2; if (HasArg(2)) { length += Arg(2); args = 3; } } }
               if (command == 0xCF) {
                  var tie = new TimelineNote { Track = track.Index, Start = time, Length = 0, Key = key, Velocity = velocity };
                  ties[key] = tie; tiesStart[key] = time;
               } else {
                  track.Notes.Add(new TimelineNote { Track = track.Index, Start = time, Length = length, Key = key, Velocity = velocity });
               }
               pc = argStart + args;
               continue;
            }
            switch (command) {
               case 0xB1: // FINE
                  track.End = time;
                  EndTies(ties, tiesStart, time, track);
                  return;
               case 0xB2: { // GOTO
                  var target = model.ReadPointer(argStart);
                  if (patternStack.Count == 0 && ticksAt.TryGetValue(target, out var targetTick) && targetTick <= time) {
                     track.LoopStart = targetTick; track.LoopEnd = time; track.End = time;
                     EndTies(ties, tiesStart, time, track);
                     return;
                  }
                  if (target < 0 || target >= model.Count) { track.End = time; return; }
                  pc = target;
                  continue;
               }
               case 0xB3: { // PATT
                  var target = model.ReadPointer(argStart);
                  if (target < 0 || target >= model.Count || patternStack.Count >= 3) { pc = argStart + 4; continue; }
                  patternStack.Push(argStart + 4);
                  pc = target;
                  continue;
               }
               case 0xB4: // PEND
                  pc = patternStack.Count > 0 ? patternStack.Pop() : argStart;
                  continue;
               case 0xB5: { // REPT
                  int count = Arg(0);
                  var target = model.ReadPointer(argStart + 1);
                  if (target < 0 || target >= model.Count) { pc = argStart + 5; continue; }
                  if (count == 0) {
                     if (ticksAt.TryGetValue(target, out var targetTick) && targetTick <= time) { track.LoopStart = targetTick; track.LoopEnd = time; track.End = time; EndTies(ties, tiesStart, time, track); return; }
                     pc = target; continue;
                  }
                  repeats.TryGetValue(pc, out var taken);
                  taken++;
                  if (taken < count) { repeats[pc] = taken; pc = target; } else { repeats[pc] = 0; pc = argStart + 5; }
                  continue;
               }
               case 0xCE: { // EOT
                  int endKey = HasArg(0) ? Arg(0) : key;
                  if (ties.TryGetValue(endKey, out var tie)) {
                     track.Notes.Add(new TimelineNote { Track = track.Index, Start = tiesStart[endKey], Length = Math.Max(1, time - tiesStart[endKey]), Key = tie.Key, Velocity = tie.Velocity });
                     ties.Remove(endKey); tiesStart.Remove(endKey);
                  }
                  pc = argStart + (HasArg(0) ? 1 : 0);
                  continue;
               }
            }
            // every other command: keep its bytes
            int argCount = command switch {
               0xB9 => 3,
               0xCD => 2,
               0xBA or 0xBB or 0xBC or 0xBD or 0xBE or 0xBF or 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC4 or 0xC5 or 0xC6 or 0xC7 or 0xC8 or 0xC9 or 0xCA or 0xCB or 0xCC => 1,
               0xB6 or 0xB7 or 0xB8 => 1,
               _ => 0,
            };
            var bytes = new byte[1 + argCount];
            bytes[0] = (byte)command;
            for (int i = 0; i < argCount; i++) bytes[1 + i] = (byte)Arg(i);
            if (command == 0xBB && firstTempo == 0) firstTempo = bytes[1] * 2;
            track.Commands.Add(new TimelineCommand { Track = track.Index, Time = time, Bytes = bytes });
            pc = argStart + argCount;
         }
         track.End = time;
         EndTies(ties, tiesStart, time, track);
      }

      private static void EndTies(Dictionary<int, TimelineNote> ties, Dictionary<int, int> starts, int time, TimelineTrack track) {
         foreach (var (key, tie) in ties) track.Notes.Add(new TimelineNote { Track = track.Index, Start = starts[key], Length = Math.Max(1, time - starts[key]), Key = tie.Key, Velocity = tie.Velocity });
         ties.Clear(); starts.Clear();
      }
   }

   /// <summary>
   /// Writes a song back from its timeline with a loop between two ticks: each track plays to the loop end and jumps back to the loop start (GOTO), or plays once and ends (FINE) when there is no loop.
   /// Patterns and repeats are written out in full (the song may grow a little), notes are written with their key and velocity, waits as the engine's wait commands.
   /// The result is the whole song: the tracks, then the header; 'headerOffset' says where the header is in it.
   /// </summary>
   public static class SongLoopWriter {
      /// <param name="trackVolumes">The volume (0-127) each track is to play at, or -1 to keep its own: every VOL of the track is scaled so that its first one becomes this (a track that is silent now, or never sets one, gets exactly this). Null: unchanged.</param>
      /// <param name="trackPrograms">The instrument (slot of the voicegroup) each track is to play, or -1 to keep its own: every VOICE of the track selects it. Null: unchanged.</param>
      /// <param name="deletedTracks">True for each track that is left out of the written song. Null: none.</param>
      /// <param name="voicegroup">The voicegroup the written header points at, or -1 for the song's own.</param>
      public static byte[] Write(SongTimeline timeline, int loopStart, int loopEnd, int baseAddress, out int headerOffset, int[] trackVolumes = null, int[] trackPrograms = null, int voicegroup = -1, bool[] deletedTracks = null) {
         var header = timeline.Header;
         var trackOffsets = new List<int>();
         var all = new List<byte>();
         bool loops = loopStart >= 0 && loopEnd > loopStart;
         foreach (var track in timeline.Tracks) {
            if (deletedTracks != null && track.Index < deletedTracks.Length && deletedTracks[track.Index]) continue;
            trackOffsets.Add(all.Count);
            var volume = trackVolumes != null && track.Index < trackVolumes.Length ? trackVolumes[track.Index] : -1;
            var program = trackPrograms != null && track.Index < trackPrograms.Length ? trackPrograms[track.Index] : -1;
            var bytes = WriteTrack(track, loops ? loopStart : -1, loops ? loopEnd : -1, baseAddress + all.Count, volume, program);
            all.AddRange(bytes);
         }
         while (all.Count % 4 != 0) all.Add(0);
         headerOffset = all.Count;
         all.Add((byte)trackOffsets.Count);
         all.Add((byte)header.BlockCount);
         all.Add((byte)header.Priority);
         all.Add((byte)header.Reverb);
         AddPointer(all, (voicegroup >= 0 ? voicegroup : header.Voicegroup) + BaseModel.PointerOffset);
         foreach (var offset in trackOffsets) AddPointer(all, baseAddress + offset + BaseModel.PointerOffset);
         return all.ToArray();
      }

      private static void AddPointer(List<byte> bytes, int value) {
         bytes.Add((byte)value); bytes.Add((byte)(value >> 8)); bytes.Add((byte)(value >> 16)); bytes.Add((byte)(value >> 24));
      }

      /// <summary>One track: its commands and notes in time order, waits between them, the loop label at loopStart and GOTO at loopEnd.</summary>
      /// <summary>The volume a track plays at now: its first VOL, or the engine's default when it never sets one.</summary>
      public const int DefaultVolume = 127;
      public static int CurrentVolume(TimelineTrack track) => track.Commands.FirstOrDefault(command => command.Command == 0xBE)?.Bytes[1] ?? DefaultVolume;

      private static byte[] WriteTrack(TimelineTrack track, int loopStart, int loopEnd, int trackAddress, int volume = -1, int program = -1) {
         var end = loopEnd >= 0 ? loopEnd : track.End;
         // what happens at each tick: commands first (an instrument change must come before the note at the same tick), then the ends of long notes (before a new note of the same key), then notes
         var events = new List<(int time, int order, byte[] bytes)>();
         var current = CurrentVolume(track);
         bool setVolume = volume >= 0 && volume != current;
         // the track's swells and fades are kept (every VOL is scaled by the same factor); a track that is silent now cannot be scaled up, so every VOL becomes the new volume
         double factor = current > 0 ? volume / (double)current : 0;
         foreach (var command in track.Commands) {
            if (command.Time > end) continue;
            var commandBytes = command.Bytes;
            if (setVolume && command.Command == 0xBE) commandBytes = new[] { (byte)0xBE, (byte)Clamp(current > 0 && command.Bytes[1] > 0 ? (int)Math.Round(command.Bytes[1] * factor) : volume) };
            if (program >= 0 && command.Command == 0xBD) commandBytes = new[] { (byte)0xBD, (byte)Clamp(program) };
            events.Add((command.Time, 0, commandBytes));
         }
         if (program >= 0 && !track.Commands.Any(command => command.Command == 0xBD)) events.Add((0, 0, new[] { (byte)0xBD, (byte)Clamp(program) }));
         if (setVolume && !track.Commands.Any(command => command.Command == 0xBE && command.Time <= Math.Max(0, track.Notes.Count == 0 ? 0 : track.Notes.Min(note => note.Start)))) {
            events.Add((0, 0, new[] { (byte)0xBE, (byte)Clamp(volume) })); // the track relied on the engine's default: now it says
         }
         foreach (var note in track.Notes) {
            if (note.Start >= end) continue; // never reached
            var length = Math.Min(note.Length, end - note.Start); // a note that reaches past the loop end is cut there: the jump back would leave it hanging otherwise
            if (length <= 0) continue;
            if (length <= 96) {
               events.Add((note.Start, 2, ShortNote(note.Key, note.Velocity, length)));
            } else {
               events.Add((note.Start, 2, new[] { (byte)0xCF, (byte)Clamp(note.Key), (byte)Clamp(note.Velocity) })); // TIE: sounds until its EOT
               events.Add((note.Start + length, 1, new[] { (byte)0xCE, (byte)Clamp(note.Key) }));
            }
         }
         var ordered = events.OrderBy(e => e.time).ThenBy(e => e.order).ToList();

         var bytes = new List<byte>();
         int time = 0;
         int loopLabel = loopStart == 0 ? 0 : -1;
         void Wait(int to) {
            while (time < to) {
               int step = Math.Min(96, to - time);
               int index = Array.FindLastIndex(MPlayDef.ClockTable, clock => clock <= step && clock > 0); // the largest wait the engine has that fits
               bytes.Add((byte)(0x80 + index));
               time += MPlayDef.ClockTable[index];
            }
         }
         void Advance(int to) {
            // the loop label sits exactly at the loop start: a wait that would cross it is split there
            if (loopStart >= 0 && loopLabel < 0 && time < loopStart && loopStart < to) { Wait(loopStart); loopLabel = bytes.Count; }
            Wait(to);
            if (loopStart >= 0 && loopLabel < 0 && time >= loopStart) loopLabel = bytes.Count;
         }
         foreach (var (eventTime, order, data) in ordered) {
            Advance(Math.Min(eventTime, end));
            // every note names its length: the engine reads up to three small bytes after a note, so a note written as key and velocity alone would swallow the key of the next one as its gate time
            bytes.AddRange(data);
         }
         Advance(end);
         if (loopStart >= 0 && loopLabel < 0) loopLabel = bytes.Count;
         if (loopStart >= 0) {
            bytes.Add(0xB2); // GOTO
            AddPointer(bytes, trackAddress + loopLabel + BaseModel.PointerOffset);
         }
         bytes.Add(0xB1); // FINE
         return bytes.ToArray();
      }

      private static int Clamp(int value) => Math.Max(0, Math.Min(127, value));

      /// <summary>N01..N96 key velocity: a length the engine has no command for takes the next shorter one, plus the difference as the gate time argument.</summary>
      private static byte[] ShortNote(int key, int velocity, int length) {
         int index = Array.IndexOf(MPlayDef.ClockTable, length);
         if (index > 0) return new[] { (byte)(0xCF + index), (byte)Clamp(key), (byte)Clamp(velocity) };
         index = Array.FindLastIndex(MPlayDef.ClockTable, clock => clock < length && clock > 0);
         return new[] { (byte)(0xCF + index), (byte)Clamp(key), (byte)Clamp(velocity), (byte)Math.Min(127, length - MPlayDef.ClockTable[index]) };
      }
   }
}
