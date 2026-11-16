using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Sound;
using HavenSoft.HexManiac.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   /// <summary>A General MIDI file is played with the instruments of the ROM that match its programs, in a voicegroup of its own, at the games' volumes.</summary>
   public class GeneralMidiImportTests : BaseViewModelTestClass {
      [SkippableFact]
      public void RealCube_BattleSilver_GetsItsInstruments_ItsOwnVoicegroup_AndGameVolumes() {
         var rom = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE"); var toml = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE_TOML"); var midiPath = Environment.GetEnvironmentVariable("HMA_MIDI");
         Skip.If(string.IsNullOrEmpty(rom) || string.IsNullOrEmpty(midiPath) || !File.Exists(midiPath), "set HMA_INTRO_CUBE, HMA_INTRO_CUBE_TOML and HMA_MIDI");
         var model = new HardcodeTablesModel(Singletons, File.ReadAllBytes(rom), new StoredMetadata(File.ReadAllLines(toml)));
         model.InitializationWorkload.Wait();
         var port = new ViewPort("CUBE.gba", model, InstantDispatch.Instance, Singletons);
         var fileSystem = new StubFileSystem();
         var tab = new SoundTab(fileSystem, port);
         var errors = new List<string>(); tab.OnError += (sender, e) => errors.Add(e);
         var midi = File.ReadAllBytes(midiPath);

         var bank = GeneralMidiBank.Build(model, tab.Voicegroups.Select(group => group.Info), null);
         Assert.True(bank.Has(56) && bank.Has(48) && bank.Has(36) && bank.Has(17) && bank.Has(47) && bank.Has(80) && bank.Has(81) && bank.Has(82) && bank.HasDrumKit);
         Assert.Equal("57 Trumpet", GeneralMidiBank.ProgramName(56));

         fileSystem.OpenFile = (description, options) => new LoadedFile("Battle_Silver_1.mid", midi);
         tab.InsertAsNewSong = true;
         var songs = tab.Songs.Count;
         tab.InsertSong.Execute(null);
         Assert.Empty(errors);
         Console.WriteLine(tab.Status);
         Assert.Equal(songs + 1, tab.Songs.Count);
         var song = tab.Songs[songs];
         Assert.Equal(10, song.Header.TrackCount);
         Assert.Contains("own new voicegroup", tab.Status);
         Assert.Contains("57 Trumpet", tab.Status);
         Assert.Contains("drum kit", tab.Status);
         Assert.Contains("Volumes brought to the games' levels", tab.Status);

         // the song's voicegroup holds exactly the instruments it plays: trumpet in slot 56 is the ROM's trumpet (the same 12 bytes as the bank's), slot 2 is blank
         var voicegroup = song.Header.Voicegroup;
         Assert.Equal(bank.Tone(56), Enumerable.Range(0, 12).Select(i => model[voicegroup + 56 * 12 + i]).ToArray());
         Assert.Equal(bank.DrumKit, Enumerable.Range(0, 12).Select(i => model[voicegroup + i]).ToArray()); // no channel plays program 0, so the drums are in slot 0 as in the games
         Assert.Equal(VoicegroupCatalog.FillerTone(), Enumerable.Range(0, 12).Select(i => model[voicegroup + 2 * 12 + i]).ToArray());

         // the song plays, and does not clip: the renderer's peak stays well under full scale
         var (left, right) = new M4aRenderer(model).Render(song.Header);
         Assert.True(left.Length > 5 * M4aRenderer.OutputRate, $"only {left.Length} samples");
         var peak = left.Max(Math.Abs);
         Console.WriteLine($"rendered {left.Length / (double)M4aRenderer.OutputRate:0.0}s, peak {peak:0.000}");
         Assert.True(peak < 0.98, $"peak {peak}");
      }

      [Fact]
      public void Summary_ReadsProgramsVolumesAndLoop() {
         // one track, two channels: a trumpet at volume 100 with expression 64, and drums
         var midi = MidiFile(
            Track(
               Meta(0, 6, "["),
               Event(0, 0xC0, 56), Event(0, 0xB0, 7, 100), Event(0, 0xB0, 11, 64),
               Event(0, 0xC9, 16),
               Event(0, 0x90, 60, 100), Event(0, 0x99, 36, 120),
               Event(96, 0x80, 60, 0), Event(0, 0x89, 36, 0),
               Meta(0, 6, "]"),
               Meta(0, 0x2F, "")));
         var summary = MidiSummary.Read(midi);
         var trumpet = summary.Channels[0]; var drums = summary.Channels[9];
         Assert.Equal(56, trumpet.Program);
         Assert.Equal(100, trumpet.Volume);
         Assert.Equal(64, trumpet.Expression);
         Assert.Equal(1, trumpet.Notes);
         Assert.True(drums.IsDrums);
         Assert.Equal(16, drums.Program);
         Assert.Equal(0, summary.LoopStart);
         Assert.Equal(96, summary.LoopEnd);
         // peak: trumpet 100/127*64/127*100/127 + drums 127/127*120/127
         Assert.Equal(100 / 127.0 * 64 / 127.0 * 100 / 127.0 + 120 / 127.0, summary.PeakLoudness, 3);
      }

      [Fact]
      public void Balance_PullsChannelsTowardTheGamesLevels_AndMasterVolumeFromThePeak() {
         var midi = MidiFile(Track(
            Event(0, 0xC0, 56), Event(0, 0xB0, 7, 127), Event(0, 0x90, 60, 127), Event(96, 0x80, 60, 0), Meta(0, 0x2F, "")));
         var summary = MidiSummary.Read(midi);
         var scales = MidiVolumeBalance.ChannelScales(summary);
         Assert.True(scales[0] < 1 && scales[0] > MidiVolumeBalance.MinScale, $"trumpet at 127 scaled by {scales[0]}"); // the games play trumpets at about 65
         Assert.Equal(1.0, scales[1]); // silent channels are left alone
         Assert.Equal(127, MidiVolumeBalance.MasterVolume(MidiVolumeBalance.TypicalPeak)); // a file as loud as the games keeps full volume
         Assert.Equal(64, MidiVolumeBalance.MasterVolume(MidiVolumeBalance.TypicalPeak * 2)); // twice as loud: half
         Assert.Equal(MidiVolumeBalance.MinMasterVolume, MidiVolumeBalance.MasterVolume(100));
      }

      [Fact]
      public void Converter_SkipsChannels_FoldsExpression_ScalesVolume_AndPutsDrumsOnSlotZero() {
         var midi = MidiFile(Track(
            Event(0, 0xC0, 56), Event(0, 0xB0, 7, 100), Event(0, 0xB0, 11, 64), Event(0, 0x90, 60, 100), Event(96, 0x80, 60, 0),
            Event(0, 0xC1, 36), Event(0, 0x91, 40, 100), Event(96, 0x81, 40, 0),
            Event(0, 0xC9, 16), Event(0, 0x99, 36, 100), Event(96, 0x89, 36, 0),
            Meta(0, 0x2F, "")));
         var options = new MidiToAgbOptions { Label = "t", SkipChannels = new HashSet<int> { 1 }, FoldExpression = true, ChannelVolumeScale = Enumerable.Repeat(0.5, 16).ToArray(), DrumChannelProgram = 0, MasterVolume = 127 };
         var result = MidiToAgb.Convert(midi, options);
         Assert.Equal(2, result.TrackCount); // channel 2 (the slap bass) is left out
         Assert.Contains("VOICE , 56", result.Text);
         Assert.DoesNotContain("VOICE , 36", result.Text);
         Assert.DoesNotContain("VOICE , 16", result.Text); // the drums select slot 0
         Assert.Contains("VOICE , 0", result.Text);
         Assert.Contains("50*t_mvl/mxv", result.Text); // 100 x 0.5
         Assert.Contains("25*t_mvl/mxv", result.Text); // then expression 64: 100 x 0.5 x 64/127 = 25
         Assert.Contains("64*t_mvl/mxv", result.Text); // the drums: 127 x 0.5, the file never set their volume
         Assert.DoesNotContain("127*t_mvl", result.Text);
      }

      #region a tiny MIDI writer

      private static byte[] MidiFile(params byte[][] tracks) {
         var bytes = new List<byte> { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, (byte)(tracks.Length > 1 ? 1 : 0), 0, (byte)tracks.Length, 0, 96 };
         foreach (var track in tracks) bytes.AddRange(track);
         return bytes.ToArray();
      }
      private static byte[] Track(params byte[][] events) {
         var body = events.SelectMany(e => e).ToList();
         var bytes = new List<byte> { (byte)'M', (byte)'T', (byte)'r', (byte)'k', (byte)(body.Count >> 24), (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count };
         bytes.AddRange(body);
         return bytes.ToArray();
      }
      private static byte[] Event(int delta, int status, int a, int b = -1) {
         var bytes = new List<byte>();
         bytes.AddRange(Vlq(delta));
         bytes.Add((byte)status); bytes.Add((byte)a);
         if ((status & 0xF0) != 0xC0 && (status & 0xF0) != 0xD0) bytes.Add((byte)Math.Max(0, b));
         return bytes.ToArray();
      }
      private static byte[] Meta(int delta, int type, string text) {
         var bytes = new List<byte>();
         bytes.AddRange(Vlq(delta));
         bytes.Add(0xFF); bytes.Add((byte)type); bytes.Add((byte)text.Length);
         bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
         return bytes.ToArray();
      }
      private static byte[] Vlq(int value) {
         if (value < 128) return new[] { (byte)value };
         return new[] { (byte)(0x80 | (value >> 7)), (byte)(value & 0x7F) };
      }

      #endregion
   }
}
