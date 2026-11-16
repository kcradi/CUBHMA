using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.Models.Sound;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace HavenSoft.HexManiac.Core.ViewModels {
   public class SnapChoice {
      public string Name { get; }
      public int Ticks { get; }
      public SnapChoice(string name, int ticks) { Name = name; Ticks = ticks; }
      public override string ToString() => Name;
   }

   /// <summary>An instrument a track can be given: a General MIDI program the ROM has an instrument for.</summary>
   public class InstrumentChoice {
      public int Program { get; }
      public string Name { get; }
      public InstrumentChoice(int program, string name) { Program = program; Name = name; }
      public override string ToString() => Name;
   }

   /// <summary>A track as the timeline draws it: its notes, its key range, a name, and the volume change the editor will write for it.</summary>
   public class TimelineTrackItem : ViewModelCore {
      public int Index { get; init; }
      public string Name { get; init; }
      public IReadOnlyList<TimelineNote> Notes { get; init; } = Array.Empty<TimelineNote>();
      public int LowestKey { get; init; }
      public int HighestKey { get; init; }
      /// <summary>The instrument the track selects first (a General MIDI program number in a song made from a MIDI file), or -1.</summary>
      public int Voice { get; init; }
      /// <summary>The name shown on the timeline: the instrument when it is known, the track's name otherwise.</summary>
      public string Label {
         get {
            if (Voice >= 0 && Voice < 128) { var name = GeneralMidiBank.ProgramName(Voice); return $"{Index + 1}: {name.Substring(name.IndexOf(' ') + 1)}"; }
            return $"{Index + 1}: {Name}";
         }
      }
      private int currentVolume = SongLoopWriter.DefaultVolume;
      /// <summary>The volume the track plays at in the ROM right now (its first VOL, or the engine's default 127), 0-127.</summary>
      public int CurrentVolume { get => currentVolume; init { currentVolume = value; volume = value; } }

      /// <summary>The instruments the track can be given (the ones this ROM has), the first being "keep".</summary>
      public IReadOnlyList<InstrumentChoice> Instruments { get; init; } = Array.Empty<InstrumentChoice>();

      private InstrumentChoice selectedInstrument;
      /// <summary>The instrument the track will be written with: null or the "keep" choice (program -1) leaves it as it is.</summary>
      public InstrumentChoice SelectedInstrument {
         get => selectedInstrument;
         set {
            if (ReferenceEquals(selectedInstrument, value)) return;
            selectedInstrument = value;
            NotifyPropertyChanged();
            NotifyPropertyChanged(nameof(IsInstrumentChanged));
         }
      }
      public int Program => selectedInstrument?.Program ?? -1;
      public bool IsInstrumentChanged => Program >= 0;

      private int volume = SongLoopWriter.DefaultVolume;
      /// <summary>The volume (0-127) the track will be written with: its swells and fades are scaled so that it starts at this. Starts at CurrentVolume.</summary>
      public int Volume { get => volume; set { value = Math.Max(0, Math.Min(127, value)); if (TryUpdate(ref volume, value)) { NotifyPropertyChanged(nameof(VolumeText)); NotifyPropertyChanged(nameof(IsVolumeChanged)); } } }
      public bool IsVolumeChanged => volume != currentVolume;
      public string VolumeText => volume.ToString();
      /// <summary>What the writer gets: -1 (keep) when the volume is what the song has.</summary>
      public int WrittenVolume => IsVolumeChanged ? volume : -1;

      private bool isDeleted;
      /// <summary>The track is left out when the song is written (and when it is played from here).</summary>
      public bool IsDeleted { get => isDeleted; set { if (TryUpdate(ref isDeleted, value)) NotifyPropertyChanged(nameof(DeleteText)); } }
      public string DeleteText => isDeleted ? "undo" : "✕";
      private StubCommand toggleDelete;
      public ICommand ToggleDelete => StubCommand(ref toggleDelete, () => IsDeleted = !IsDeleted);

      public void Reset() { Volume = currentVolume; SelectedInstrument = Instruments.Count > 0 ? Instruments[0] : null; IsDeleted = false; }
   }

   /// <summary>
   /// The loop editor of the Sound tab: the selected song on a timeline (every note of every track against bars and beats), with a loop start and a loop end that can be
   /// dragged on the timeline or typed as bar:beat:tick, and written into the song (each track then jumps back from the end to the start, like the '[' and ']' markers of mid2agb).
   /// </summary>
   public class SongLoopEditor : ViewModelCore {
      private readonly IDataModel model;
      private readonly Func<ModelDelta> tokenFactory;
      private readonly Func<GeneralMidiBank> bankFactory;

      public SongLoopEditor(IDataModel model, Func<ModelDelta> tokenFactory, Func<GeneralMidiBank> bankFactory = null) {
         this.model = model;
         this.tokenFactory = tokenFactory;
         this.bankFactory = bankFactory;
      }

      private List<InstrumentChoice> instruments;
      /// <summary>The instruments a track can be given: "keep" and every General MIDI program this ROM has an instrument for (see GeneralMidiBank).</summary>
      public IReadOnlyList<InstrumentChoice> Instruments {
         get {
            if (instruments != null) return instruments;
            instruments = new List<InstrumentChoice> { new InstrumentChoice(-1, "(keep)") };
            var bank = bankFactory?.Invoke();
            if (bank != null) {
               for (int program = 1; program < GeneralMidiBank.Programs; program++) if (bank.Has(program)) instruments.Add(new InstrumentChoice(program, GeneralMidiBank.ProgramName(program)));
               if (bank.HasDrumKit) instruments.Add(new InstrumentChoice(GeneralMidiBank.DrumSlot, "drum kit"));
            }
            return instruments;
         }
      }

      public const int TicksPerBeat = SongTimeline.TicksPerBeat;
      public const int TicksPerBar = SongTimeline.TicksPerBar;

      private SongTimeline timeline;
      public SongTimeline Timeline => timeline;
      /// <summary>The name of the song on the timeline (for naming a voicegroup made for it).</summary>
      public string SongName { get; set; }

      public ObservableCollection<TimelineTrackItem> Tracks { get; } = new();

      private bool hasSong;
      /// <summary>Is there a song on the timeline?</summary>
      public bool HasSong { get => hasSong; private set => Set(ref hasSong, value); }

      private int length;
      /// <summary>The length of the song in ticks (the last note or the end of the longest track).</summary>
      public int Length { get => length; private set { if (TryUpdate(ref length, value)) { NotifyPropertyChanged(nameof(Bars)); NotifyPropertyChanged(nameof(LengthText)); } } }
      public int Bars => (Length + TicksPerBar - 1) / TicksPerBar;
      public string LengthText => timeline == null ? string.Empty : $"{Bars} bars, {timeline.Describe(Length)} ({Length * timeline.SecondsPerTick:0.0} s at {timeline.Tempo} bpm)";

      private int tempo;
      public int Tempo { get => tempo; private set => Set(ref tempo, value); }

      private int loopStart = -1, loopEnd = -1;
      /// <summary>The tick the song jumps back to, or -1 for no loop.</summary>
      public int LoopStart {
         get => loopStart;
         set {
            value = ClampTick(value);
            if (!TryUpdate(ref loopStart, value)) return;
            if (loopEnd >= 0 && loopEnd <= loopStart) LoopEnd = Math.Min(Length, loopStart + TicksPerBar);
            else if (loopStart >= 0 && loopEnd < 0) LoopEnd = Length; // a start alone: the song loops from its end, until an end is set
            LoopChanged();
         }
      }
      /// <summary>The tick the song jumps back from (the end of the loop), or -1.</summary>
      public int LoopEnd {
         get => loopEnd;
         set {
            value = ClampTick(value);
            if (!TryUpdate(ref loopEnd, value)) return;
            if (loopStart >= 0 && loopStart >= loopEnd) LoopStart = Math.Max(0, loopEnd - TicksPerBar);
            LoopChanged();
         }
      }
      private int ClampTick(int tick) => tick < 0 ? -1 : Math.Max(0, Math.Min(Math.Max(Length, 1), tick));

      public bool HasLoop => loopStart >= 0 && loopEnd > loopStart;
      /// <summary>The loop the song has in the ROM right now (what the timeline read), to tell an edit from the original.</summary>
      public int SavedLoopStart { get; private set; } = -1;
      public int SavedLoopEnd { get; private set; } = -1;
      public bool IsChanged => HasSong && (loopStart != SavedLoopStart || loopEnd != SavedLoopEnd || Tracks.Any(track => track.IsVolumeChanged || track.IsInstrumentChanged || track.IsDeleted));
      public bool HasVolumeChanges => Tracks.Any(track => track.IsVolumeChanged);
      public bool HasInstrumentChanges => Tracks.Any(track => !track.IsDeleted && track.IsInstrumentChanged);
      public bool HasDeletedTracks => Tracks.Any(track => track.IsDeleted);
      private int[] TrackVolumes => Tracks.Select(track => track.WrittenVolume).ToArray();
      private int[] TrackPrograms => Tracks.Select(track => track.Program).ToArray();
      private bool[] DeletedTracks => Tracks.Select(track => track.IsDeleted).ToArray();

      /// <summary>
      /// The song as the editor has it, to be put at 'address': when a track was given another instrument, first a copy of the song's voicegroup with that instrument in the slot
      /// the track now selects (so the song keeps every other instrument and no other song is changed), then the tracks and the header (at address + headerOffset).
      /// </summary>
      public byte[] EditedSongBytes(int address, out int headerOffset) {
         headerOffset = 0;
         if (timeline == null) return null;
         var all = new List<byte>();
         var voicegroup = -1;
         if (HasInstrumentChanges) {
            var bank = bankFactory?.Invoke();
            var copy = new byte[VoicegroupCatalog.Length];
            var filler = VoicegroupCatalog.FillerTone();
            for (int slot = 0; slot < GeneralMidiBank.Programs; slot++) {
               var source = timeline.Header.Voicegroup + slot * VoicegroupCatalog.EntrySize;
               for (int i = 0; i < VoicegroupCatalog.EntrySize; i++) copy[slot * VoicegroupCatalog.EntrySize + i] = source + i < model.Count && source >= 0 ? model[source + i] : filler[i];
            }
            foreach (var track in Tracks) {
               if (!track.IsInstrumentChanged || track.IsDeleted) continue;
               var tone = track.Program == GeneralMidiBank.DrumSlot ? bank?.DrumKit : bank?.Tone(track.Program);
               if (tone == null) continue;
               Array.Copy(tone, 0, copy, track.Program * VoicegroupCatalog.EntrySize, tone.Length);
            }
            all.AddRange(copy);
            voicegroup = address;
         }
         var songStart = address + all.Count;
         var song = SongLoopWriter.Write(timeline, loopStart, loopEnd, songStart, out var songHeaderOffset, TrackVolumes, TrackPrograms, voicegroup, DeletedTracks);
         all.AddRange(song);
         headerOffset = all.Count - song.Length + songHeaderOffset;
         return all.ToArray();
      }

      public string LoopStartText {
         get => loopStart < 0 || timeline == null ? string.Empty : timeline.Describe(loopStart);
         set { if (SongTimeline.TryParsePosition(value, out var tick)) LoopStart = tick; else if (string.IsNullOrWhiteSpace(value)) LoopStart = -1; NotifyPropertyChanged(); }
      }
      public string LoopEndText {
         get => loopEnd < 0 || timeline == null ? string.Empty : timeline.Describe(loopEnd);
         set { if (SongTimeline.TryParsePosition(value, out var tick)) LoopEnd = tick; else if (string.IsNullOrWhiteSpace(value)) LoopEnd = loopStart >= 0 ? Length : -1; NotifyPropertyChanged(); }
      }
      public string LoopSummary {
         get {
            if (!HasSong) return string.Empty;
            if (!HasLoop) return "No loop: the song plays once and stops." + (IsChanged ? " Changes not written yet." : string.Empty);
            var seconds = (loopEnd - loopStart) * timeline.SecondsPerTick;
            return $"Loops from {timeline.Describe(loopStart)} back from {timeline.Describe(loopEnd)}: {(loopEnd - loopStart) / (double)TicksPerBar:0.##} bars ({seconds:0.0} s){(IsChanged ? " - changes not written yet" : string.Empty)}.";
         }
      }

      private void LoopChanged() {
         NotifyPropertyChanged(nameof(HasLoop));
         NotifyPropertyChanged(nameof(IsChanged));
         NotifyPropertyChanged(nameof(LoopStartText));
         NotifyPropertyChanged(nameof(LoopEndText));
         NotifyPropertyChanged(nameof(LoopSummary));
         applyLoop?.RaiseCanExecuteChanged();
         removeLoop?.RaiseCanExecuteChanged();
         revertLoop?.RaiseCanExecuteChanged();
      }

      private int snap = TicksPerBeat;
      /// <summary>The grid the markers snap to when dragged: 96 a bar, 24 a beat, 12 an eighth, 6 a sixteenth, 1 free.</summary>
      public int Snap { get => snap; set => Set(ref snap, Math.Max(1, value)); }
      public IReadOnlyList<SnapChoice> SnapChoices { get; } = new[] { new SnapChoice("bar", TicksPerBar), new SnapChoice("beat", TicksPerBeat), new SnapChoice("1/8", TicksPerBeat / 2), new SnapChoice("1/16", TicksPerBeat / 4), new SnapChoice("tick", 1) };
      public int SnapTick(int tick) => Snap <= 1 ? tick : (int)Math.Round(tick / (double)Snap) * Snap;

      private double pixelsPerBeat = 12;
      /// <summary>The zoom of the timeline.</summary>
      public double PixelsPerBeat { get => pixelsPerBeat; set => Set(ref pixelsPerBeat, Math.Max(2, Math.Min(96, value))); }

      private int cursorTick = 0;
      /// <summary>Where playing starts from (a tick): click on the timeline to put it somewhere, 'Play from here' plays from there.</summary>
      public int CursorTick {
         get => cursorTick;
         set {
            value = Math.Max(0, Math.Min(Math.Max(0, Length), value));
            if (!TryUpdate(ref cursorTick, value)) return;
            NotifyPropertyChanged(nameof(CursorText));
         }
      }
      public string CursorText => timeline == null ? string.Empty : timeline.Describe(cursorTick);

      /// <summary>Raised to play the song as the editor has it (loop and volumes, written or not) from a tick.</summary>
      public event EventHandler<int> RequestPlayFrom;
      private StubCommand playFromCursor, playFromStart;
      public ICommand PlayFromCursor => StubCommand(ref playFromCursor, () => RequestPlayFrom?.Invoke(this, cursorTick), () => HasSong);
      public ICommand PlayFromStart => StubCommand(ref playFromStart, () => RequestPlayFrom?.Invoke(this, 0), () => HasSong);

      private int playheadTick = -1;
      /// <summary>Where the song is while it plays (a tick, 24 to a beat), or -1 when nothing plays. A song past its loop end shows inside the loop, where the player is.</summary>
      public int PlayheadTick {
         get => playheadTick;
         set {
            if (value >= 0 && HasLoop && value >= loopEnd) value = loopStart + (value - loopStart) % Math.Max(1, loopEnd - loopStart);
            if (!TryUpdate(ref playheadTick, value)) return;
            NotifyPropertyChanged(nameof(IsPlaying));
            NotifyPropertyChanged(nameof(PlayheadText));
            startAtPlayhead?.RaiseCanExecuteChanged();
            endAtPlayhead?.RaiseCanExecuteChanged();
         }
      }
      public bool IsPlaying => playheadTick >= 0;
      public string PlayheadText => playheadTick < 0 || timeline == null ? string.Empty : timeline.Describe(playheadTick);

      private StubCommand startAtPlayhead, endAtPlayhead;
      /// <summary>Puts the loop start where the song is playing right now, on the snap grid.</summary>
      public ICommand StartAtPlayhead => StubCommand(ref startAtPlayhead, () => { var tick = SnapTick(playheadTick); if (loopEnd >= 0 && tick >= loopEnd) LoopEnd = -1; LoopStart = tick; }, () => IsPlaying);
      /// <summary>Puts the loop end where the song is playing right now, on the snap grid.</summary>
      public ICommand EndAtPlayhead => StubCommand(ref endAtPlayhead, () => { var tick = SnapTick(playheadTick); if (tick <= Math.Max(0, loopStart)) return; if (loopStart < 0) LoopStart = 0; LoopEnd = tick; }, () => IsPlaying);

      private string status = string.Empty;
      public string Status { get => status; set => Set(ref status, value); }

      public event EventHandler<string> OnError;
      /// <summary>Raised after the loop is written: the song's bytes moved, so the Sound tab reads its songs again.</summary>
      public event EventHandler<int> SongWritten;

      private StubCommand applyLoop, removeLoop, revertLoop;
      public ICommand ApplyLoop => StubCommand(ref applyLoop, () => Write(loopStart, loopEnd), () => HasSong && IsChanged && (HasLoop || loopStart < 0));
      public ICommand RemoveLoop => StubCommand(ref removeLoop, () => { LoopStart = -1; LoopEnd = -1; }, () => HasSong && (loopStart >= 0 || loopEnd >= 0));
      public ICommand RevertLoop => StubCommand(ref revertLoop, () => { loopStart = SavedLoopStart; loopEnd = SavedLoopEnd; foreach (var track in Tracks) track.Reset(); NotifyPropertyChanged(nameof(LoopStart)); NotifyPropertyChanged(nameof(LoopEnd)); LoopChanged(); }, () => IsChanged);

      /// <summary>Puts a song on the timeline (null clears it).</summary>
      public void Load(SongHeader header, IReadOnlyList<string> trackNames = null) {
         Tracks.Clear();
         timeline = null;
         PlayheadTick = -1;
         if (header == null || header.TrackCount == 0) {
            HasSong = false; Length = 0; Tempo = 0; loopStart = loopEnd = -1; SavedLoopStart = SavedLoopEnd = -1;
            Status = string.Empty;
            LoopChanged();
            return;
         }
         try {
            timeline = SongTimeline.Read(model, header);
         } catch (Exception e) {
            HasSong = false; Status = "The song could not be read: " + e.Message; LoopChanged();
            return;
         }
         for (int i = 0; i < timeline.Tracks.Count; i++) {
            var track = timeline.Tracks[i];
            var voice = track.Voice;
            var name = trackNames != null && i < trackNames.Count ? trackNames[i] : $"Track {i + 1}";
            var firstVolume = SongLoopWriter.CurrentVolume(track);
            var item = new TimelineTrackItem { Index = i, Name = name, Notes = track.Notes, LowestKey = track.LowestKey, HighestKey = track.HighestKey, Voice = voice, CurrentVolume = firstVolume, Instruments = Instruments };
            item.SelectedInstrument = Instruments[0];
            item.PropertyChanged += (sender, e) => { if (e.PropertyName is nameof(TimelineTrackItem.Volume) or nameof(TimelineTrackItem.SelectedInstrument) or nameof(TimelineTrackItem.IsDeleted)) LoopChanged(); };
            Tracks.Add(item);
         }
         cursorTick = 0;
         NotifyPropertyChanged(nameof(CursorTick));
         NotifyPropertyChanged(nameof(CursorText));
         Length = timeline.Length;
         Tempo = timeline.Tempo;
         loopStart = SavedLoopStart = timeline.HasLoop ? timeline.LoopStart : -1;
         loopEnd = SavedLoopEnd = timeline.HasLoop ? timeline.LoopEnd : -1;
         HasSong = true;
         Status = string.Empty;
         NotifyPropertyChanged(nameof(LoopStart));
         NotifyPropertyChanged(nameof(LoopEnd));
         NotifyPropertyChanged(nameof(LengthText));
         LoopChanged();
      }

      /// <summary>Writes the song back with the loop: the new bytes go to free space, the song table entry points at the new header.</summary>
      public bool Write(int start, int end) {
         if (timeline == null) return false;
         if (!(start < 0 && end < 0) && !(start >= 0 && end > start)) { OnError?.Invoke(this, "The loop end must come after the loop start."); return false; }
         var token = tokenFactory();
         loopStart = start; loopEnd = end;
         var bytes = EditedSongBytes(0, out _);
         if (!VoicegroupEditor.TryAllocate(model, token, bytes.Length + 4, out var address, out var error)) { OnError?.Invoke(this, "Could not find room for the song: " + error); return false; }
         while (address % 4 != 0) address++;
         bytes = EditedSongBytes(address, out var headerOffset);
         token.ChangeData(model, address, bytes);
         string voicegroupName = null;
         if (HasInstrumentChanges) voicegroupName = VoicegroupEditor.NameWrittenVoicegroup(model, token, address, SongName ?? "song");
         var header = timeline.Header;
         // the table entry (and anything else) that pointed at the old header points at the new one
         var oldHeader = header.Address;
         var newHeader = address + headerOffset;
         var sources = model.GetNextRun(oldHeader) is IFormattedRun run && run.Start == oldHeader ? run.PointerSources?.ToList() : null;
         if (sources == null || sources.Count == 0) sources = FindPointersTo(oldHeader);
         foreach (var source in sources) model.WritePointer(token, source, newHeader);
         var changedVolumes = Tracks.Where(track => !track.IsDeleted && track.IsVolumeChanged).Select(track => $"track {track.Index + 1} at {track.VolumeText}").ToList();
         var changedInstruments = Tracks.Where(track => !track.IsDeleted && track.IsInstrumentChanged).Select(track => $"track {track.Index + 1} plays {track.SelectedInstrument.Name}").ToList();
         var deleted = Tracks.Where(track => track.IsDeleted).Select(track => (track.Index + 1).ToString()).ToList();
         Status = (start < 0 ? "The song plays once and stops." : $"The loop is written: from {timeline.Describe(start)} back from {timeline.Describe(end)}.")
            + (deleted.Count > 0 ? $" Tracks removed: {string.Join(", ", deleted)} (the song has {Tracks.Count - deleted.Count} now)." : string.Empty)
            + (changedVolumes.Count > 0 ? $" Volumes: {string.Join(", ", changedVolumes)}." : string.Empty)
            + (changedInstruments.Count > 0 ? $" Instruments: {string.Join(", ", changedInstruments)}; the song got a copy of its voicegroup with them at {address:X6}{(voicegroupName == null ? string.Empty : " (" + voicegroupName + ")")}." : string.Empty);
         SongWritten?.Invoke(this, newHeader);
         return true;
      }

      private List<int> FindPointersTo(int address) {
         var result = new List<int>();
         var pointer = address + BaseModel.PointerOffset;
         for (int i = 0; i + 4 <= model.Count; i += 4) {
            if (model.ReadMultiByteValue(i, 4) == pointer) result.Add(i);
         }
         return result;
      }
   }
}
