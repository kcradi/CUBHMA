using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Sound;
using HavenSoft.HexManiac.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   /// <summary>The loop editor: a song on a timeline, a loop that can be set between two ticks and written back, and read again as the same loop.</summary>
   public class SongLoopEditorTests : BaseViewModelTestClass {
      [SkippableFact]
      public void RealCube_EverySong_ReadsToATimeline_AndTheFirstLoopingSong_RoundTrips() {
         var rom = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE"); var toml = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE_TOML");
         Skip.If(string.IsNullOrEmpty(rom), "set HMA_INTRO_CUBE and HMA_INTRO_CUBE_TOML");
         var model = new HardcodeTablesModel(Singletons, File.ReadAllBytes(rom), new StoredMetadata(File.ReadAllLines(toml)));
         model.InitializationWorkload.Wait();
         var port = new ViewPort("CUBE.gba", model, InstantDispatch.Instance, Singletons);
         var tab = new SoundTab(new StubFileSystem(), port);
         int looping = 0, withNotes = 0;
         SongItem sample = null;
         foreach (var song in tab.Songs) {
            if (song.Header == null || song.Header.TrackCount == 0) continue;
            var timeline = SongTimeline.Read(model, song.Header);
            if (timeline.AllNotes.Any()) withNotes++;
            if (timeline.HasLoop) { looping++; if (sample == null || timeline.AllNotes.Count() > SongTimeline.Read(model, sample.Header).AllNotes.Count()) sample = song; } // the busiest looping song
         }
         Console.WriteLine($"{withNotes} songs with notes, {looping} looping");
         Assert.True(withNotes > 300);
         Assert.True(looping > 200);

         // the first looping song: read, write back with the same loop, and the written song reads the same
         tab.SelectedSong = sample;
         var loop = tab.Loop;
         Assert.True(loop.HasSong);
         Assert.True(loop.HasLoop);
         var before = SongTimeline.Read(model, sample.Header);
         var (start, end) = (loop.LoopStart, loop.LoopEnd);
         Console.WriteLine($"song {sample.Index} {sample.Name}: {loop.LengthText}, loop {loop.LoopStartText} .. {loop.LoopEndText}, tempo {loop.Tempo}");
         Assert.False(loop.IsChanged);
         var oldHeader = sample.Header.Address;
         Assert.True(loop.Write(start, end));
         var again = tab.Songs[sample.Index];
         Assert.NotEqual(oldHeader, again.Header.Address);
         Assert.Equal(sample.Header.Voicegroup, again.Header.Voicegroup);
         var after = SongTimeline.Read(model, again.Header);
         Assert.Equal(before.LoopStart, after.LoopStart);
         Assert.Equal(before.LoopEnd, after.LoopEnd);
         Assert.Equal(before.Tracks.Count, after.Tracks.Count);
         for (int i = 0; i < before.Tracks.Count; i++) {
            var mine = before.Tracks[i].Notes.Where(note => note.Start < end).Select(note => (note.Start, Math.Min(note.Length, end - note.Start), note.Key, note.Velocity)).OrderBy(n => n).ToList();
            var theirs = after.Tracks[i].Notes.Select(note => (note.Start, note.Length, note.Key, note.Velocity)).OrderBy(n => n).ToList();
            Assert.Equal(mine, theirs);
         }
         // the song still plays
         var (left, _) = new M4aRenderer(model).Render(again.Header);
         Assert.True(left.Length > M4aRenderer.OutputRate);

         // forty more looping songs round-trip note for note (patterns and repeats unrolled)
         int checkedSongs = 0;
         foreach (var song in tab.Songs.Where(song => song.Header != null && song.Header.TrackCount > 0).Skip(100)) {
            var original = SongTimeline.Read(model, song.Header);
            if (!original.HasLoop) continue;
            var bytes = SongLoopWriter.Write(original, original.LoopStart, original.LoopEnd, 0x1F00000, out var headerOffset);
            Array.Copy(bytes, 0, model.RawData, 0x1F00000, bytes.Length);
            Assert.True(SongHeader.TryRead(model, 0x1F00000 + headerOffset, out var copy));
            var copied = SongTimeline.Read(model, copy);
            Assert.Equal((original.LoopStart, original.LoopEnd), (copied.LoopStart, copied.LoopEnd));
            for (int i = 0; i < original.Tracks.Count; i++) {
               var e = original.LoopEnd;
               var mine = original.Tracks[i].Notes.Where(note => note.Start < e).Select(note => (note.Start, Math.Min(note.Length, e - note.Start), note.Key, note.Velocity)).OrderBy(n => n).ToList();
               var theirs = copied.Tracks[i].Notes.Select(note => (note.Start, note.Length, note.Key, note.Velocity)).OrderBy(n => n).ToList();
               var firstDifference = Enumerable.Range(0, Math.Min(mine.Count, theirs.Count)).Where(n => !mine[n].Equals(theirs[n])).Select(n => $"#{n} {mine[n]} vs {theirs[n]}").FirstOrDefault();
               Assert.True(mine.SequenceEqual(theirs), $"song {song.Index} {song.Name} track {i}: {mine.Count} vs {theirs.Count} notes, {firstDifference}");
            }
            if (++checkedSongs == 40) break;
         }
         Assert.Equal(40, checkedSongs);
      }

      [SkippableFact]
      public void RealCube_PlayingASong_GivesATickMap_AndThePlayheadSetsTheMarkersOnTheGrid() {
         var rom = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE"); var toml = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE_TOML");
         Skip.If(string.IsNullOrEmpty(rom), "set HMA_INTRO_CUBE and HMA_INTRO_CUBE_TOML");
         var model = new HardcodeTablesModel(Singletons, File.ReadAllBytes(rom), new StoredMetadata(File.ReadAllLines(toml)));
         model.InitializationWorkload.Wait();
         var port = new ViewPort("CUBE.gba", model, InstantDispatch.Instance, Singletons);
         var tab = new SoundTab(new StubFileSystem(), port);
         var song = tab.Songs.First(item => item.Header != null && item.Header.TrackCount > 0 && SongTimeline.Read(model, item.Header).HasLoop && SongTimeline.Read(model, item.Header).Length > 4 * 96);
         tab.SelectedSong = song;
         byte[] played = null;
         tab.RequestPlayWav += (sender, wav) => played = wav;
         tab.PlaySong.Execute(null);
         Assert.NotNull(played);
         var map = tab.PlaybackTickMap;
         Assert.NotNull(map);
         Assert.True(map.Count > 100);
         Assert.Equal(0, map[0].tick);
         Assert.True(map[map.Count - 1].tick > map[0].tick);
         for (int i = 1; i < map.Count; i++) Assert.True(map[i].sample >= map[i - 1].sample && map[i].tick >= map[i - 1].tick);
         // one second in at the song's tempo: about tempo/60*24 ticks
         var timeline = SongTimeline.Read(model, song.Header);
         var oneSecond = map.Last(entry => entry.sample <= M4aRenderer.OutputRate).tick;
         Assert.InRange(oneSecond, timeline.Tempo * 24 / 60 - 6, timeline.Tempo * 24 / 60 + 6);

         var loop = tab.Loop;
         loop.Snap = SongLoopEditor.TicksPerBar;
         loop.PlayheadTick = 96 + 40; // bar 2, beat 2 and a bit
         Assert.True(loop.IsPlaying);
         Assert.Equal("2:2:16", loop.PlayheadText);
         loop.StartAtPlayhead.Execute(null);
         Assert.Equal(96, loop.LoopStart); // snapped to the bar
         loop.PlayheadTick = 3 * 96 + 50;
         loop.EndAtPlayhead.Execute(null);
         Assert.Equal(4 * 96, loop.LoopEnd);
         // past the loop end the playhead shows inside the loop, where the player is
         loop.PlayheadTick = 4 * 96 + 24;
         Assert.Equal(96 + 24, loop.PlayheadTick);
         loop.PlayheadTick = -1;
         Assert.False(loop.IsPlaying);
      }

      [SkippableFact]
      public void RealCube_TrackEditor_PlaysFromTheCursor_ScalesAVolume_AndGivesATrackAnotherInstrument() {
         var rom = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE"); var toml = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE_TOML");
         Skip.If(string.IsNullOrEmpty(rom), "set HMA_INTRO_CUBE and HMA_INTRO_CUBE_TOML");
         var model = new HardcodeTablesModel(Singletons, File.ReadAllBytes(rom), new StoredMetadata(File.ReadAllLines(toml)));
         model.InitializationWorkload.Wait();
         var port = new ViewPort("CUBE.gba", model, InstantDispatch.Instance, Singletons);
         var tab = new SoundTab(new StubFileSystem(), port);
         var errors = new List<string>(); tab.OnError += (sender, e) => errors.Add(e);
         var song = tab.Songs.First(item => item.Header != null && item.Header.TrackCount > 2 && SongTimeline.Read(model, item.Header).HasLoop && SongTimeline.Read(model, item.Header).Length > 8 * 96);
         tab.SelectedSong = song;
         var loop = tab.Loop;
         Assert.True(loop.Instruments.Count > 100);
         Assert.Equal(-1, loop.Instruments[0].Program);

         // play from bar 5: the recording is shorter by the time to bar 5, and the playhead starts there
         byte[] full = null, fromBar5 = null;
         tab.RequestPlayWav += (sender, wav) => { if (fromBar5 == null && full != null) fromBar5 = wav; else full ??= wav; };
         loop.PlayFromStart.Execute(null);
         loop.CursorTick = 4 * 96;
         Assert.Equal("5:1:00", loop.CursorText);
         loop.PlayFromCursor.Execute(null);
         Assert.Empty(errors);
         Assert.NotNull(full); Assert.NotNull(fromBar5);
         Assert.True(fromBar5.Length < full.Length - 44);
         Assert.True(tab.PlaybackStartSample > 0);
         Assert.True(tab.PlaybackTickMap.Last(entry => entry.sample <= tab.PlaybackStartSample).tick <= 4 * 96);
         Assert.True(tab.PlaybackTickMap.First(entry => entry.sample >= tab.PlaybackStartSample).tick >= 4 * 96);

         // volume: track 1 down to 50 (the slider shows the song's own volume to start with), track 2 plays the trumpet, the last track is removed
         var original = SongTimeline.Read(model, song.Header);
         var volumeBefore = loop.Tracks[0].CurrentVolume;
         Assert.Equal(volumeBefore, loop.Tracks[0].Volume);
         var target = volumeBefore > 60 ? 50 : 100;
         loop.Tracks[0].Volume = target;
         var trackCount = loop.Tracks.Count;
         loop.Tracks[trackCount - 1].ToggleDelete.Execute(null);
         Assert.True(loop.Tracks[trackCount - 1].IsDeleted && loop.HasDeletedTracks, "deleted");
         loop.Tracks[1].SelectedInstrument = loop.Instruments.First(choice => choice.Program == 56);
         Assert.True(loop.IsChanged && loop.HasVolumeChanges && loop.HasInstrumentChanges, "changed");
         var oldVoicegroup = song.Header.Voicegroup;
         Assert.True(loop.Write(loop.LoopStart, loop.LoopEnd), string.Join("; ", errors));
         var written = tab.Songs[song.Index];
         Assert.NotEqual(oldVoicegroup, written.Header.Voicegroup); // a copy of the voicegroup, with the trumpet in slot 56
         var bank = GeneralMidiBank.Build(model, tab.Voicegroups.Select(group => group.Info), null);
         Assert.Equal(bank.Tone(56), Enumerable.Range(0, 12).Select(i => model[written.Header.Voicegroup + 56 * 12 + i]).ToArray());
         for (int slot = 0; slot < 128; slot++) {
            if (slot == 56) continue;
            for (int i = 0; i < 12; i++) Assert.Equal(model[oldVoicegroup + slot * 12 + i], model[written.Header.Voicegroup + slot * 12 + i]); // every other instrument stays
         }
         var after = SongTimeline.Read(model, written.Header);
         Assert.True(after.Tracks[1].Commands.Where(command => command.Command == 0xBD).All(command => command.Bytes[1] == 56));
         var firstVolume = after.Tracks[0].Commands.First(command => command.Command == 0xBE).Bytes[1];
         Assert.Equal(target, firstVolume);
         Assert.Equal(trackCount - 1, after.Tracks.Count);
         Assert.Equal(trackCount - 1, loop.Tracks.Count);
         Assert.Equal(target, loop.Tracks[0].CurrentVolume); // the slider shows the written volume now
         Assert.Equal(target, loop.Tracks[0].Volume);
         Assert.Contains("Tracks removed", tab.Status);
         Assert.Equal(original.Tracks[0].Notes.Count(note => note.Start < original.LoopEnd), after.Tracks[0].Notes.Count);
         Assert.True(tab.Voicegroups.Any(group => group.Info.Address == written.Header.Voicegroup)); // the copy is a voicegroup of the ROM now, with a name
         Assert.Contains("plays 57 Trumpet", tab.Status);
         Assert.False(loop.IsChanged); // reloaded after the write: what it shows is what the ROM has

         // a track muted and then brought back is heard again
         loop.Tracks[0].Volume = 0;
         Assert.True(loop.Write(loop.LoopStart, loop.LoopEnd), string.Join("; ", errors));
         Assert.Equal(0, loop.Tracks[0].CurrentVolume);
         loop.Tracks[0].Volume = 90;
         Assert.True(loop.Write(loop.LoopStart, loop.LoopEnd), string.Join("; ", errors));
         Assert.Equal(90, loop.Tracks[0].CurrentVolume);
         var back = SongTimeline.Read(model, tab.Songs[song.Index].Header);
         Assert.True(back.Tracks[0].Commands.Where(command => command.Command == 0xBE).All(command => command.Bytes[1] == 90));

         // a loop start alone is a loop (to the end of the song), so it can be written
         loop.RemoveLoop.Execute(null);
         Assert.False(loop.HasLoop);
         loop.LoopStart = 2 * 96;
         Assert.True(loop.HasLoop);
         Assert.Equal(loop.Length, loop.LoopEnd);
         Assert.True(loop.ApplyLoop.CanExecute(null));
      }

      [Fact]
      public void Timeline_Positions() {
         Assert.True(SongTimeline.TryParsePosition("3:2:12", out var tick)); Assert.Equal(2 * 96 + 24 + 12, tick);
         Assert.True(SongTimeline.TryParsePosition("1:1", out tick)); Assert.Equal(0, tick);
         Assert.True(SongTimeline.TryParsePosition("200", out tick)); Assert.Equal(200, tick);
         Assert.False(SongTimeline.TryParsePosition("0:1", out _));
         Assert.False(SongTimeline.TryParsePosition("1:5", out _));
         var timeline = new SongTimeline();
         Assert.Equal("3:2:12", timeline.Describe(2 * 96 + 24 + 12));
      }

      [Fact]
      public void SmallSong_LoopIsWritten_AndReadBack_AndNotesAreCutAtTheLoopEnd() {
         // a one-track song: VOICE 1, four quarter notes, then a long tie into bar 2
         var model = new PokemonModel(Enumerable.Repeat((byte)0xFF, 0x2000).ToArray(), singletons: Singletons);
         var port = new ViewPort("song.gba", model, InstantDispatch.Instance, Singletons);
         var track = new byte[] { 0xBD, 1, 0xBB, 75, 0xE7, 60, 100, 0x98, 0xE7, 62, 100, 0x98, 0xE7, 64, 100, 0x98, 0xE7, 65, 100, 0x98, 0xCF, 67, 100, 0xB0, 0xB0, 0xCE, 67, 0xB1 };
         Array.Copy(track, 0, model.RawData, 0x100, track.Length);
         var header = new byte[] { 1, 0, 0, 0, 0x00, 0x02, 0x00, 0x08, 0x00, 0x01, 0x00, 0x08 };
         Array.Copy(header, 0, model.RawData, 0x200, header.Length);
         Assert.True(SongHeader.TryRead(model, 0x200, out var songHeader));
         var timeline = SongTimeline.Read(model, songHeader);
         Assert.Equal(150, timeline.Tempo);
         Assert.Equal(5, timeline.Tracks[0].Notes.Count);
         var tie = timeline.Tracks[0].Notes[4];
         Assert.Equal((96, 192, 67), (tie.Start, tie.Length, tie.Key));
         Assert.False(timeline.HasLoop);
         Assert.Equal(96 + 192, timeline.Length);

         var editor = new SongLoopEditor(model, () => port.CurrentChange);
         editor.Load(songHeader);
         Assert.True(editor.HasSong);
         editor.LoopStartText = "1:2";
         editor.LoopEndText = "2:1";
         Assert.Equal(24, editor.LoopStart);
         Assert.Equal(96, editor.LoopEnd);
         Assert.True(editor.IsChanged);
         int headerAddress = -1;
         editor.SongWritten += (sender, e) => headerAddress = e;
         var errors = new List<string>(); editor.OnError += (sender, e) => errors.Add(e);
         Assert.True(editor.Write(editor.LoopStart, editor.LoopEnd), string.Join("; ", errors));
         Assert.True(headerAddress >= 0 && headerAddress != 0x200, $"header at {headerAddress:X}");

         Assert.True(SongHeader.TryRead(model, headerAddress, out var newSong));
         var after = SongTimeline.Read(model, newSong);
         Assert.True(after.HasLoop);
         Assert.Equal(24, after.LoopStart);
         Assert.Equal(96, after.LoopEnd);
         Assert.Equal(4, after.Tracks[0].Notes.Count); // the tie after the loop end is never reached
         Assert.Equal(new[] { 0, 24, 48, 72 }, after.Tracks[0].Notes.Select(note => note.Start).ToArray());
         Assert.True(after.Tracks[0].Commands.Any(command => command.Command == 0xBD && command.Bytes[1] == 1)); // the instrument stays
      }
   }
}
