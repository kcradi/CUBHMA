using HavenSoft.HexManiac.Core.Models.Sound;
using HavenSoft.HexManiac.Core.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HavenSoft.HexManiac.WPF.Controls {
   /// <summary>
   /// The timeline of the loop editor: a ruler of bars and beats on top, one lane per track below it with the track's notes as bars (higher keys higher in the lane, louder notes brighter),
   /// the loop as a shaded span with two handles, "start" and "end", that are dragged to set it. The width follows the song and the zoom (PixelsPerBeat), the control sits in a ScrollViewer.
   /// Clicking in the ruler moves the nearer handle there; Shift-dragging ignores the snap.
   /// </summary>
   public class SongTimelineControl : FrameworkElement {
      public const double RulerHeight = 22, LaneHeight = 26, LaneGap = 2, LabelWidth = 0, HandleWidth = 10; // the track names, volumes and instruments sit in a panel to the left of the control

      public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(nameof(Editor), typeof(SongLoopEditor), typeof(SongTimelineControl), new FrameworkPropertyMetadata(null, EditorChanged));
      public SongLoopEditor Editor { get => (SongLoopEditor)GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

      private static void EditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
         var self = (SongTimelineControl)d;
         if (e.OldValue is SongLoopEditor old) { old.PropertyChanged -= self.EditorPropertyChanged; old.Tracks.CollectionChanged -= self.TracksChanged; }
         if (e.NewValue is SongLoopEditor editor) { editor.PropertyChanged += self.EditorPropertyChanged; editor.Tracks.CollectionChanged += self.TracksChanged; }
         self.InvalidateMeasure();
         self.InvalidateVisual();
      }

      private void EditorPropertyChanged(object sender, PropertyChangedEventArgs e) {
         if (e.PropertyName is nameof(SongLoopEditor.PixelsPerBeat) or nameof(SongLoopEditor.Length) or nameof(SongLoopEditor.HasSong)) InvalidateMeasure();
         if (e.PropertyName == nameof(SongLoopEditor.PlayheadTick) && Editor?.PlayheadTick >= 0) {
            // keep the playhead in view while the song plays (the control sits in a ScrollViewer)
            var x = TickToX(Editor.PlayheadTick);
            BringIntoView(new Rect(x - 60, 0, 120, 1));
         }
         InvalidateVisual();
      }

      private void TracksChanged(object sender, NotifyCollectionChangedEventArgs e) { InvalidateMeasure(); InvalidateVisual(); }

      public SongTimelineControl() {
         ClipToBounds = true;
         Focusable = true;
         ToolTip = "Click in the tracks to put the cursor there; double-click (or Space) plays the song from the cursor. Drag the start and end handles to set the loop (hold Shift to ignore the snap); click in the ruler to move the nearer handle there. Mouse wheel with Ctrl zooms.";
      }

      private double PixelsPerTick => (Editor?.PixelsPerBeat ?? 12) / SongLoopEditor.TicksPerBeat;
      private double TickToX(int tick) => LabelWidth + tick * PixelsPerTick;
      private int XToTick(double x) => (int)Math.Round((x - LabelWidth) / PixelsPerTick);

      protected override Size MeasureOverride(Size availableSize) {
         var editor = Editor;
         if (editor == null || !editor.HasSong) return new Size(Math.Min(availableSize.Width, 400), RulerHeight + LaneHeight);
         var width = LabelWidth + (editor.Bars + 1) * SongLoopEditor.TicksPerBar * PixelsPerTick + HandleWidth;
         var height = RulerHeight + editor.Tracks.Count * (LaneHeight + LaneGap) + 4;
         return new Size(width, height);
      }

      private static SolidColorBrush Brush(string name) => (SolidColorBrush)Application.Current.Resources.MergedDictionaries[0][name];

      private static readonly Color[] trackColors = {
         Color.FromRgb(0x4F, 0xA3, 0xE8), Color.FromRgb(0xE8, 0x8A, 0x4F), Color.FromRgb(0x6F, 0xCF, 0x6F), Color.FromRgb(0xD9, 0x6B, 0xC9), Color.FromRgb(0xE8, 0xD0, 0x4F),
         Color.FromRgb(0x4F, 0xD8, 0xD0), Color.FromRgb(0xE8, 0x5A, 0x5A), Color.FromRgb(0xA8, 0x8F, 0xE8), Color.FromRgb(0x9F, 0xC9, 0x4F), Color.FromRgb(0xE8, 0x9F, 0xC0),
         Color.FromRgb(0x7F, 0xB8, 0xE8), Color.FromRgb(0xE8, 0xB0, 0x7F), Color.FromRgb(0x8F, 0xE8, 0xA8), Color.FromRgb(0xC0, 0x8F, 0xE8), Color.FromRgb(0xE8, 0xE8, 0x8F), Color.FromRgb(0x8F, 0xE8, 0xE8),
      };

      protected override void OnRender(DrawingContext dc) {
         var editor = Editor;
         var background = Brush(nameof(Theme.Background));
         dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
         if (editor == null || !editor.HasSong) {
            var hint = new FormattedText("Select a song with notes to see it here.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brush(nameof(Theme.Secondary)), 1.0);
            dc.DrawText(hint, new Point(8, 6));
            return;
         }
         var primary = Brush(nameof(Theme.Primary));
         var secondary = Brush(nameof(Theme.Secondary));
         var backlight = Brush(nameof(Theme.Backlight));
         var accent = Brush(nameof(Theme.Accent));
         var beatPen = new Pen(backlight, 1);
         var barPen = new Pen(secondary, 1) { DashStyle = DashStyles.Dot };
         var typeface = new Typeface("Segoe UI");
         double width = ActualWidth, pixelsPerTick = PixelsPerTick;
         int ticksPerBar = SongLoopEditor.TicksPerBar, ticksPerBeat = SongLoopEditor.TicksPerBeat;
         int bars = editor.Bars + 1;

         // the loop: a shaded span behind everything
         if (editor.HasLoop) {
            var loopBrush = new SolidColorBrush(Color.FromArgb(0x30, accent.Color.R, accent.Color.G, accent.Color.B));
            dc.DrawRectangle(loopBrush, null, new Rect(TickToX(editor.LoopStart), RulerHeight, Math.Max(1, (editor.LoopEnd - editor.LoopStart) * pixelsPerTick), ActualHeight - RulerHeight));
         }

         // the ruler: a tick every beat, a line and a number every bar
         dc.DrawRectangle(backlight, null, new Rect(LabelWidth, 0, width - LabelWidth, RulerHeight));
         double labelEvery = Math.Max(1, Math.Ceiling(48 / (ticksPerBar * pixelsPerTick))); // bar numbers at least 48 px apart
         for (int bar = 0; bar < bars; bar++) {
            var x = TickToX(bar * ticksPerBar);
            dc.DrawLine(barPen, new Point(x, 0), new Point(x, ActualHeight));
            if (bar % (int)labelEvery == 0) {
               var text = new FormattedText((bar + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, primary, 1.0);
               dc.DrawText(text, new Point(x + 3, 2));
            }
            if (ticksPerBeat * pixelsPerTick >= 6) {
               for (int beat = 1; beat < SongTimeline.BeatsPerBar; beat++) {
                  var bx = TickToX(bar * ticksPerBar + beat * ticksPerBeat);
                  dc.DrawLine(beatPen, new Point(bx, RulerHeight - 6), new Point(bx, RulerHeight));
                  dc.DrawLine(beatPen, new Point(bx, RulerHeight), new Point(bx, ActualHeight));
               }
            }
         }
         dc.DrawLine(new Pen(secondary, 1), new Point(0, RulerHeight), new Point(width, RulerHeight));

         // the lanes
         double y = RulerHeight + 2;
         foreach (var track in editor.Tracks) {
            var color = trackColors[track.Index % trackColors.Length];
            var laneRect = new Rect(LabelWidth, y, width - LabelWidth, LaneHeight);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x14, color.R, color.G, color.B)), null, laneRect);
            if (LabelWidth > 0) {
               var label = new FormattedText(TrackLabel(track), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, primary, 1.0) { MaxTextWidth = LabelWidth - 6, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
               dc.DrawText(label, new Point(4, y + (LaneHeight - label.Height) / 2));
            }
            // notes: key range of the track spread over the lane height, velocity as brightness
            int low = track.LowestKey, high = Math.Max(track.HighestKey, track.LowestKey + 1);
            double noteHeight = Math.Max(2, Math.Min(6, (LaneHeight - 4) / Math.Max(1, high - low + 1)));
            foreach (var note in track.Notes) {
               var x = TickToX(note.Start);
               var w = Math.Max(1.5, note.Length * pixelsPerTick - 0.5);
               if (x + w < LabelWidth || x > width) continue;
               var ny = y + 2 + (LaneHeight - 4 - noteHeight) * (1 - (note.Key - low) / (double)Math.Max(1, high - low));
               byte alpha = (byte)(0x70 + 0x8F * Math.Min(127, note.Velocity) / 127);
               if (track.IsDeleted) alpha /= 4; // a removed track: its notes fade, and a line goes through the lane
               dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), null, new Rect(x, ny, w, noteHeight));
            }
            if (track.IsDeleted) dc.DrawLine(new Pen(secondary, 1), new Point(LabelWidth, y + LaneHeight / 2), new Point(width, y + LaneHeight / 2));
            y += LaneHeight + LaneGap;
         }

         // the end of the song
         var endX = TickToX(editor.Length);
         dc.DrawLine(new Pen(secondary, 1) { DashStyle = DashStyles.Dash }, new Point(endX, RulerHeight), new Point(endX, ActualHeight));

         // the cursor: where playing starts
         if (editor.CursorTick >= 0) {
            var cx = TickToX(editor.CursorTick);
            dc.DrawLine(new Pen(primary, 1) { DashStyle = DashStyles.Dash }, new Point(cx, RulerHeight), new Point(cx, ActualHeight));
            dc.DrawGeometry(primary, null, new PathGeometry(new[] { new PathFigure(new Point(cx, RulerHeight - 7), new[] { new LineSegment(new Point(cx + 6, RulerHeight - 3.5), true), new LineSegment(new Point(cx, RulerHeight), true) }, true) }));
         }

         // the playhead, while the song plays
         if (editor.PlayheadTick >= 0) {
            var px = TickToX(editor.PlayheadTick);
            dc.DrawLine(new Pen(primary, 2), new Point(px, 0), new Point(px, ActualHeight));
            dc.DrawGeometry(primary, null, new PathGeometry(new[] { new PathFigure(new Point(px - 5, 0), new[] { new LineSegment(new Point(px + 5, 0), true), new LineSegment(new Point(px, 7), true) }, true) }));
         }

         // the handles
         if (editor.LoopStart >= 0) DrawHandle(dc, TickToX(editor.LoopStart), "start", accent, true, typeface);
         if (editor.LoopEnd >= 0) DrawHandle(dc, TickToX(editor.LoopEnd), "end", accent, false, typeface);
      }

      private static string TrackLabel(TimelineTrackItem track) {
         if (track.Voice >= 0 && track.Voice < 128) {
            var name = GeneralMidiBank.ProgramName(track.Voice);
            var space = name.IndexOf(' ');
            return $"{track.Index + 1}: {name.Substring(space + 1)}";
         }
         return $"{track.Index + 1}: {track.Name}";
      }

      private void DrawHandle(DrawingContext dc, double x, string text, SolidColorBrush accent, bool start, Typeface typeface) {
         dc.DrawLine(new Pen(accent, 2), new Point(x, 0), new Point(x, ActualHeight));
         var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, Brush(nameof(Theme.Background)), 1.0);
         var w = label.Width + 8;
         var rect = start ? new Rect(x, 0, w, RulerHeight - 4) : new Rect(x - w, 0, w, RulerHeight - 4);
         dc.DrawRoundedRectangle(accent, null, rect, 3, 3);
         dc.DrawText(label, new Point(rect.X + 4, rect.Y + (rect.Height - label.Height) / 2));
      }

      #region Mouse: drag the handles

      private enum Handle { None, Start, End }
      private Handle dragging = Handle.None;

      private Handle HandleAt(Point point) {
         var editor = Editor;
         if (editor == null || !editor.HasSong) return Handle.None;
         double startX = editor.LoopStart >= 0 ? TickToX(editor.LoopStart) : double.NaN, endX = editor.LoopEnd >= 0 ? TickToX(editor.LoopEnd) : double.NaN;
         double distanceStart = double.IsNaN(startX) ? double.MaxValue : Math.Abs(point.X - startX), distanceEnd = double.IsNaN(endX) ? double.MaxValue : Math.Abs(point.X - endX);
         // in the ruler the handle's label counts too
         if (point.Y < RulerHeight) {
            if (!double.IsNaN(startX) && point.X >= startX && point.X <= startX + 40) distanceStart = 0;
            if (!double.IsNaN(endX) && point.X <= endX && point.X >= endX - 32) distanceEnd = 0;
         }
         if (Math.Min(distanceStart, distanceEnd) > HandleWidth) return Handle.None;
         return distanceStart <= distanceEnd ? Handle.Start : Handle.End;
      }

      protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
         base.OnMouseLeftButtonDown(e);
         var editor = Editor;
         if (editor == null || !editor.HasSong) return;
         Focus();
         var point = e.GetPosition(this);
         if (point.X < LabelWidth) return;
         dragging = HandleAt(point);
         if (dragging == Handle.None) {
            var tick = Snap(XToTick(point.X), e);
            if (point.Y >= RulerHeight) {
               // click in the tracks: the cursor goes there; a double-click plays from there
               editor.CursorTick = tick;
               if (e.ClickCount == 2 && editor.PlayFromCursor.CanExecute(null)) editor.PlayFromCursor.Execute(null);
               e.Handled = true;
               return;
            }
            // click in the ruler: the nearer handle comes here; without a loop the first click sets the start, the second the end
            if (editor.LoopStart < 0) { editor.LoopStart = tick; dragging = Handle.Start; }
            else if (editor.LoopEnd < 0) { if (tick > editor.LoopStart) { editor.LoopEnd = tick; dragging = Handle.End; } else { editor.LoopStart = tick; dragging = Handle.Start; } }
            else {
               var nearerStart = Math.Abs(tick - editor.LoopStart) <= Math.Abs(tick - editor.LoopEnd);
               if (nearerStart) { editor.LoopStart = tick; dragging = Handle.Start; } else { editor.LoopEnd = tick; dragging = Handle.End; }
            }
         }
         if (dragging != Handle.None) { CaptureMouse(); e.Handled = true; }
      }

      protected override void OnMouseMove(MouseEventArgs e) {
         base.OnMouseMove(e);
         var editor = Editor;
         if (editor == null) return;
         var point = e.GetPosition(this);
         if (dragging == Handle.None) {
            Cursor = HandleAt(point) != Handle.None || point.Y < RulerHeight && point.X >= LabelWidth ? Cursors.SizeWE : Cursors.Arrow;
            return;
         }
         var tick = Snap(XToTick(point.X), e);
         if (dragging == Handle.Start) editor.LoopStart = Math.Min(tick, editor.LoopEnd < 0 ? tick : editor.LoopEnd - 1);
         else editor.LoopEnd = Math.Max(tick, editor.LoopStart + 1);
         e.Handled = true;
      }

      protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
         base.OnMouseLeftButtonUp(e);
         if (dragging == Handle.None) return;
         dragging = Handle.None;
         ReleaseMouseCapture();
         e.Handled = true;
      }

      protected override void OnKeyDown(KeyEventArgs e) {
         base.OnKeyDown(e);
         var editor = Editor;
         if (editor == null) return;
         if (e.Key == Key.OemOpenBrackets && editor.StartAtPlayhead.CanExecute(null)) { editor.StartAtPlayhead.Execute(null); e.Handled = true; }
         if (e.Key == Key.OemCloseBrackets && editor.EndAtPlayhead.CanExecute(null)) { editor.EndAtPlayhead.Execute(null); e.Handled = true; }
         if (e.Key == Key.Space && editor.PlayFromCursor.CanExecute(null)) { editor.PlayFromCursor.Execute(null); e.Handled = true; }
         if (e.Key == Key.Home && editor.HasSong) { editor.CursorTick = 0; e.Handled = true; }
      }

      protected override void OnMouseWheel(MouseWheelEventArgs e) {
         if (Editor != null && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
            Editor.PixelsPerBeat *= e.Delta > 0 ? 1.25 : 0.8;
            e.Handled = true;
            return;
         }
         base.OnMouseWheel(e);
      }

      private int Snap(int tick, InputEventArgs e) {
         var editor = Editor;
         tick = Math.Max(0, Math.Min(Math.Max(1, editor.Length), tick));
         return (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? tick : editor.SnapTick(tick);
      }

      #endregion
   }
}
