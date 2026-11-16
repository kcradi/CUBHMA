using HavenSoft.HexManiac.Core.ViewModels;
using System;
using System.IO;
using System.Media;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace HavenSoft.HexManiac.WPF.Controls {
   public partial class SoundView {
      private SoundPlayer player;
      private MemoryStream playerStream;
      // following a playing song on the loop editor's timeline: SoundPlayer has no position of its own, so the time since Play and the renderer's tick map give it
      private readonly DispatcherTimer playhead = new() { Interval = TimeSpan.FromMilliseconds(30) };
      private readonly Stopwatch playClock = new();
      private System.Collections.Generic.IReadOnlyList<(int sample, int tick)> tickMap;
      private double playSeconds;
      private int playStartSample; // where in the rendered song the recording starts (play from the cursor)

      private SoundTab ViewModel => DataContext as SoundTab;

      public SoundView() {
         InitializeComponent();
         DataContextChanged += (sender, e) => {
            if (e.OldValue is SoundTab old) { old.RequestPlayWav -= PlayWav; old.RequestStopPlayback -= StopRequested; }
            if (e.NewValue is SoundTab vm) { vm.RequestPlayWav += PlayWav; vm.RequestStopPlayback += StopRequested; }
         };
         Unloaded += (sender, e) => StopPlayback();
         playhead.Tick += PlayheadTick;
      }

      private void PlayheadTick(object sender, EventArgs e) {
         var loop = ViewModel?.Loop;
         if (loop == null || tickMap == null || tickMap.Count == 0 || !playClock.IsRunning) { StopPlayhead(); return; }
         var seconds = playClock.Elapsed.TotalSeconds;
         if (seconds >= playSeconds) { StopPlayhead(); return; }
         var sample = playStartSample + (int)(seconds * HavenSoft.HexManiac.Core.Models.Sound.M4aRenderer.OutputRate);
         // the last frame that starts at or before this sample
         int low = 0, high = tickMap.Count - 1;
         while (low < high) { int mid = (low + high + 1) / 2; if (tickMap[mid].sample <= sample) low = mid; else high = mid - 1; }
         loop.PlayheadTick = tickMap[low].tick;
      }

      private void StopPlayhead() {
         playhead.Stop();
         playClock.Reset();
         tickMap = null;
         if (ViewModel?.Loop != null) ViewModel.Loop.PlayheadTick = -1;
      }

      private void PlayWav(object sender, byte[] wav) {
         try {
            StopPlayback();
            playerStream = new MemoryStream(wav);
            player = new SoundPlayer(playerStream);
            player.Load();
            player.Play();
            tickMap = ViewModel?.PlaybackTickMap;
            playStartSample = ViewModel?.PlaybackStartSample ?? 0;
            if (tickMap != null && tickMap.Count > 0) {
               playSeconds = (wav.Length - 44) / 4.0 / HavenSoft.HexManiac.Core.Models.Sound.M4aRenderer.OutputRate;
               playClock.Restart();
               playhead.Start();
            }
         } catch (Exception ex) {
            MessageBox.Show("Could not play the sound: " + ex.Message, "CUBHMA", MessageBoxButton.OK, MessageBoxImage.Warning);
         }
      }

      private void StopRequested(object sender, EventArgs e) => StopPlayback();

      private void StopPlayback() {
         try {
            player?.Stop();
            player?.Dispose();
            playerStream?.Dispose();
         } catch (Exception) {
            // nothing to clean up
         }
         player = null;
         playerStream = null;
         StopPlayhead();
      }

      private void SongNameKeyDown(object sender, KeyEventArgs e) {
         if (e.Key != Key.Enter || sender is not System.Windows.Controls.TextBox box) return;
         // commit the name now: the binding updates when the box loses focus
         box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
         e.Handled = true;
      }

      // a song the view model selects (the one 'Add new song' just made, at the end of a long list) has to be brought into view
      private void SongSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
         if (sender is System.Windows.Controls.ListBox list && list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
      }

      private void CryDoubleClick(object sender, MouseButtonEventArgs e) {
         if (ViewModel?.PlayCry.CanExecute(null) == true) ViewModel.PlayCry.Execute(null);
      }
   }
}
