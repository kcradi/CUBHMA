using HavenSoft.HexManiac.Core.Models.Runs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models.CubeUpdate {
   /// <summary>
   /// A ROM seen as a graph: nodes are the structs of tables (one element of an array run) and the other runs (text, sprites, scripts...),
   /// edges are the pointers that the loaded metadata knows about.
   /// A node is identified by its path from a named anchor (the anchor name, then for every step: the element index of an array, or the offset of the pointer that leads on).
   /// Paths do not depend on addresses, so they stay the same when a build moves things around.
   /// A node's "scalar hash" is a hash of its bytes with every pointer left out: it changes when the data of the node itself is edited, not when something it points to moves.
   /// The graph works on a snapshot of the data, so it stays valid while the model it came from is changed.
   /// </summary>
   public class RomGraph {
      public IDataModel Model { get; }
      public byte[] Data { get; }
      private readonly IFormattedRun[] runs;           // sorted by start
      private readonly int[] pointerSources;           // sorted
      private readonly int[] pointerDestinations;      // same order as pointerSources
      private readonly Dictionary<int, ulong> subtreeMemo = new();

      public RomGraph(IDataModel model) {
         Model = model;
         Data = (byte[])model.RawData.Clone();
         var known = model.All<IFormattedRun>().Where(run => run.Length > 0).ToList();
         // what the model found inside the blocks of a tileset (words that look like pointers) is block data, not structure
         var ranges = AddBlocksets(model, known).OrderBy(range => range.start).ToArray();
         bool Inside(int address) {
            int lo = 0, hi = ranges.Length - 1;
            while (lo <= hi) { var mid = (lo + hi) / 2; if (ranges[mid].start <= address) lo = mid + 1; else hi = mid - 1; }
            return hi >= 0 && address > ranges[hi].start && address < ranges[hi].start + ranges[hi].length;
         }
         if (ranges.Length > 0) known.RemoveAll(run => (run is PointerRun || run is NoInfoRun) && Inside(run.Start));
         runs = known.OrderBy(run => run.Start).ToArray();
         var list = new List<(int src, int dst)>();
         foreach (var run in runs) {
            if (run.PointerSources == null) continue;
            foreach (var source in run.PointerSources) if (!Inside(source)) list.Add((source, run.Start));
         }
         list.Sort();
         pointerSources = list.Select(p => p.src).ToArray();
         pointerDestinations = list.Select(p => p.dst).ToArray();
      }

      public IReadOnlyList<IFormattedRun> Runs => runs;

      private readonly Dictionary<int, int> scriptLengths = new();
      /// <summary>The length of a run as the update sees it: a script is as long as its commands (the model's run for a script is 1 byte).</summary>
      public int LengthOf(IFormattedRun run) {
         if (run is not IScriptStartRun script) return run.Length;
         if (scriptLengths.TryGetValue(run.Start, out var known)) return known;
         int length;
         try { length = Math.Max(run.Length, Model.GetScriptLength(script, new Dictionary<int, int>())); } catch (Exception) { length = run.Length; }
         scriptLengths[run.Start] = length;
         return length;
      }

      /// <summary>
      /// The blocks of a tileset and their attributes: the metadata knows only a pointer to each (blockset&lt;&gt;, attributes&lt;&gt;), so the model has a 1-byte run there,
      /// and the map editor writes the blocks raw. With 1 byte, an edit of the blocks would be invisible to the update and the new CUBE's blocks would win (the maps that use
      /// the edited blocks then show the wrong tiles). Here they get their real length, the way the map editor reads them, so edited blocks are carried over like any other data.
      /// </summary>
      private static List<(int start, int length)> AddBlocksets(IDataModel model, List<IFormattedRun> known) {
         var added = new List<(int start, int length)>();
         var byStart = new Dictionary<int, int>();
         for (int i = 0; i < known.Count; i++) byStart[known[i].Start] = i;
         foreach (var run in known.ToList()) {
            if (run is not ITableRun table || table.ElementCount != 1 || table.ElementLength < 24) continue;
            int blocksetOffset = -1, attributesOffset = -1, offset = 0;
            foreach (var segment in table.ElementContent) {
               if (segment.Name == "blockset") blocksetOffset = offset;
               if (segment.Name == "attributes") attributesOffset = offset;
               offset += segment.Length;
            }
            if (blocksetOffset < 0 || attributesOffset < 0) continue;
            int blockStart = model.ReadPointer(run.Start + blocksetOffset), attributeStart = model.ReadPointer(run.Start + attributesOffset);
            if (blockStart < 0 || blockStart >= model.Count || attributeStart < 0 || attributeStart >= model.Count) continue;
            var blockset = new global::HexManiac.Core.Models.Runs.Sprites.BlocksetModel(model, run.Start);
            var blockCount = blockset.IsSecondary ? 1024 - blockset.PrimaryBlocks : blockset.PrimaryBlocks;
            global::HexManiac.Core.Models.Runs.Sprites.BlocksetModel.EstimateBlockCount(model, ref blockCount, blockStart, attributeStart);
            if (Replace(known, byStart, blockStart, blockCount * 16)) added.Add((blockStart, blockCount * 16));
            if (Replace(known, byStart, attributeStart, blockCount * blockset.BytesPerAttribute)) added.Add((attributeStart, blockCount * blockset.BytesPerAttribute));
         }
         return added;
      }

      private static bool Replace(List<IFormattedRun> known, Dictionary<int, int> byStart, int start, int length) {
         if (length <= 0) return false;
         if (byStart.TryGetValue(start, out var index)) {
            if (known[index] is FakeRun) return false;      // done already (a tileset that two layouts use)
            if (known[index] is not NoInfoRun) return false; // the metadata describes it: keep that
            known[index] = new FakeRun(start, length, known[index].PointerSources);
         } else {
            byStart[start] = known.Count;
            known.Add(new FakeRun(start, length));
         }
         return true;
      }

      /// <summary>The run that starts exactly at the address, or null.</summary>
      public IFormattedRun RunAt(int address) {
         int lo = 0, hi = runs.Length - 1;
         while (lo <= hi) {
            var mid = (lo + hi) / 2;
            if (runs[mid].Start == address) return runs[mid];
            if (runs[mid].Start < address) lo = mid + 1; else hi = mid - 1;
         }
         return null;
      }

      /// <summary>The run that contains the address (it may start earlier), or null.</summary>
      public IFormattedRun RunContaining(int address) {
         int lo = 0, hi = runs.Length - 1, best = -1;
         while (lo <= hi) {
            var mid = (lo + hi) / 2;
            if (runs[mid].Start <= address) { best = mid; lo = mid + 1; } else hi = mid - 1;
         }
         if (best < 0) return null;
         return address < runs[best].Start + LengthOf(runs[best]) ? runs[best] : null;
      }

      /// <summary>The pointers whose own address is in [start, start+length): (address of the pointer, address it points to), in address order.</summary>
      public IEnumerable<(int source, int destination)> PointersIn(int start, int length) {
         var index = Array.BinarySearch(pointerSources, start);
         if (index < 0) index = ~index;
         while (index < pointerSources.Length && pointerSources[index] < start + length) {
            yield return (pointerSources[index], pointerDestinations[index]);
            index++;
         }
      }

      /// <summary>The address that the known pointer at 'source' leads to, or -1 if there is no known pointer there.</summary>
      public int PointerAt(int source) {
         var index = Array.BinarySearch(pointerSources, source);
         return index >= 0 ? pointerDestinations[index] : -1;
      }

      public int PointerCount => pointerSources.Length;

      #region hashing

      private const ulong FnvOffset = 14695981039346656037UL, FnvPrime = 1099511628211UL;
      public static ulong Mix(ulong h, ulong value) {
         h ^= value; h *= FnvPrime; h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
         return h;
      }

      /// <summary>
      /// True for a 4-byte-aligned word that is a pointer to the start of a run, even if the metadata does not know it as a pointer.
      /// Those are left out of hashes too, so a hash does not depend on how much the metadata knows. (A word that merely has the right top byte is not enough: base stats can look like that.)
      /// </summary>
      public enum Heuristic { None, Loose, RunAtTarget }
      public static Heuristic PointerHeuristic { get; set; } = Heuristic.None;

      public bool IsPointerWord(int address) {
         if (PointerHeuristic == Heuristic.None) return false;
         if ((address & 3) != 0 || address + 4 > Data.Length) return false;
         var top = Data[address + 3];
         if (top != 0x08 && top != 0x09) return false;
         var value = Data[address] | (Data[address + 1] << 8) | (Data[address + 2] << 16) | ((top & 1) << 24);
         return value < Data.Length && (PointerHeuristic == Heuristic.Loose || RunAt(value) != null);
      }

      /// <summary>Hash of the bytes in [start, start+length) with every pointer left out (known pointers, and aligned words that look like one).</summary>
      public ulong ScalarHash(int start, int length) {
         var h = Mix(FnvOffset, (ulong)length);
         var position = start;
         var end = start + length;
         using var known = PointersIn(start, length).GetEnumerator();
         var hasKnown = known.MoveNext();
         while (position < end) {
            while (hasKnown && known.Current.source < position) hasKnown = known.MoveNext();
            if (hasKnown && known.Current.source == position) {
               h = Mix(h, 0xABCD);
               position += 4;
               continue;
            }
            if (IsPointerWord(position) && position + 4 <= end && !(hasKnown && known.Current.source < position + 4)) {
               h = Mix(h, 0xABCD);
               position += 4;
               continue;
            }
            h ^= Data[position]; h *= FnvPrime;
            position++;
         }
         return h;
      }

      /// <summary>Hash of a run and everything it points to (pointer values do not count, only what they lead to).</summary>
      public ulong SubtreeHash(IFormattedRun run) => SubtreeHash(run.Start, LengthOf(run), new HashSet<int>());

      private ulong SubtreeHash(int start, int length, HashSet<int> inProgress) {
         if (subtreeMemo.TryGetValue(start, out var known)) return known;
         if (!inProgress.Add(start)) return 0x5E1F;
         var h = Mix(FnvOffset, (ulong)length);
         var position = start;
         var end = start + length;
         using var pointers = PointersIn(start, length).GetEnumerator();
         var hasKnown = pointers.MoveNext();
         while (position < end) {
            while (hasKnown && pointers.Current.source < position) hasKnown = pointers.MoveNext();
            if (hasKnown && pointers.Current.source == position) {
               var target = RunAt(pointers.Current.destination);
               h = Mix(h, target == null ? 0xFEED : SubtreeHash(target.Start, LengthOf(target), inProgress));
               position += 4;
               continue;
            }
            if (IsPointerWord(position) && position + 4 <= end && !(hasKnown && pointers.Current.source < position + 4)) {
               h = Mix(h, 0xABCD);
               position += 4;
               continue;
            }
            h ^= Data[position]; h *= FnvPrime;
            position++;
         }
         inProgress.Remove(start);
         subtreeMemo[start] = h;
         return h;
      }

      #endregion

      #region walking

      public record Node(ulong Path, string Name, int Start, int Length, ulong ScalarHash, int Depth);

      /// <summary>The named anchors with a run, in name order.</summary>
      public IEnumerable<(string name, IFormattedRun run)> NamedRuns() {
         foreach (var name in Model.Anchors.OrderBy(n => n, StringComparer.Ordinal)) {
            var address = Model.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, name);
            if (address < 0 || address >= Data.Length) continue;
            var run = RunAt(address);
            if (run != null) yield return (name, run);
         }
      }

      public static ulong PathOf(string anchor) {
         ulong h = FnvOffset;
         foreach (var c in anchor) h = Mix(h, c);
         return h;
      }

      public static ulong ElementPath(ulong path, int index) => Mix(path, (ulong)index + 1);
      public static ulong ChildPath(ulong path, int pointerOffset) => Mix(path, 0x1000000UL + (ulong)pointerOffset);

      public const int MaxDepth = 8;

      /// <summary>
      /// Visits every node below a run: the elements of an array run (and what they point to), or the run itself (and what it points to).
      /// A run that is reached on two paths is visited on both.
      /// </summary>
      public void Walk(IFormattedRun run, ulong path, string name, int depth, Action<Node> visit) => Walk(run, path, name, depth, visit, new HashSet<int>());

      private void Walk(IFormattedRun run, ulong path, string name, int depth, Action<Node> visit, HashSet<int> ancestors) {
         if (!ancestors.Add(run.Start)) return;
         try {
            if (run is ArrayRun array && array.ElementLength > 0) {
               for (int i = 0; i < array.ElementCount; i++) {
                  var start = array.Start + i * array.ElementLength;
                  var elementPath = ElementPath(path, i);
                  visit(new Node(elementPath, name, start, array.ElementLength, ScalarHash(start, array.ElementLength), depth));
                  if (depth < MaxDepth) WalkChildren(start, array.ElementLength, elementPath, name, depth, visit, ancestors);
               }
            } else {
               var length = LengthOf(run);
               visit(new Node(path, name, run.Start, length, ScalarHash(run.Start, length), depth));
               if (depth < MaxDepth) WalkChildren(run.Start, length, path, name, depth, visit, ancestors);
            }
         } finally {
            ancestors.Remove(run.Start);
         }
      }

      private void WalkChildren(int start, int length, ulong path, string name, int depth, Action<Node> visit, HashSet<int> ancestors) {
         foreach (var (source, destination) in PointersIn(start, length).ToList()) {
            var target = RunAt(destination);
            if (target == null) continue;
            Walk(target, ChildPath(path, source - start), name, depth + 1, visit, ancestors);
         }
      }

      #endregion
   }
}
