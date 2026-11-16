using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.CubeUpdate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   /// <summary>
   /// A map whose tileset the author changed to another tileset of CUBE must, after Update CUBE, use that other tileset of the new CUBE;
   /// the tileset it used before must stay as the new CUBE has it (every other map that uses it would change otherwise).
   /// </summary>
   public class UpdateCubeTilesetTests {
      private static int ReadPointer(byte[] data, int address) => (data[address] | (data[address + 1] << 8) | (data[address + 2] << 16) | (data[address + 3] << 24)) - 0x08000000;

      [SkippableFact]
      public void RealCube_MapPointedAtAnotherTileset_KeepsBothTilesetsIntact() => Run("HMA_INTRO_CUBE", "HMA_INTRO_CUBE_TOML", false);

      /// <summary>The same from an older CUBE: the author's blocks and the repointed map arrive in the new CUBE, whose own tilesets stay as they are.</summary>
      [SkippableFact]
      public void OldCube_MapPointedAtAnotherTileset_KeepsBothTilesetsIntact() => Run("UPD_OLD", "UPD_OLD_TOML", true);

      private static void Run(string romVariable, string tomlVariable, bool old) {
         var rom = Environment.GetEnvironmentVariable(romVariable); var toml = Environment.GetEnvironmentVariable(tomlVariable);
         var newRom = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE"); var newToml = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE_TOML");
         var fp = Environment.GetEnvironmentVariable("UPD_FP");
         Skip.If(string.IsNullOrEmpty(rom) || !File.Exists(rom) || string.IsNullOrEmpty(newRom) || string.IsNullOrEmpty(fp) || !File.Exists(fp), $"set {romVariable}, {tomlVariable}, HMA_INTRO_CUBE, HMA_INTRO_CUBE_TOML and UPD_FP (cube_versions.fp)");
         var singletons = BaseViewModelTestClass.Singletons;
         var cubeRom = File.ReadAllBytes(rom); var cubeToml = File.ReadAllLines(toml);
         var known = CubeFingerprint.Read(File.ReadAllBytes(fp));

         var hacked = CubeUpdateService.Load(singletons, (byte[])cubeRom.Clone(), cubeToml);
         var layouts = hacked.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "data.maps.layouts");
         var layoutCount = ((HavenSoft.HexManiac.Core.Models.Runs.ITableRun)hacked.GetNextRun(layouts)).ElementCount;
         // secondary tilesets by how many layouts use them
         var uses = new Dictionary<int, List<int>>();
         for (int i = 0; i < layoutCount; i++) {
            var layout = ReadPointer(hacked.RawData, layouts + i * 4);
            if (layout < 0 || layout >= hacked.Count) continue;
            var tileset = ReadPointer(hacked.RawData, layout + 20);
            if (tileset < 0 || tileset >= hacked.Count) continue;
            if (!uses.TryGetValue(tileset, out var list)) uses[tileset] = list = new List<int>();
            list.Add(layout);
         }
         var ordered = uses.OrderByDescending(pair => pair.Value.Count).ToList();
         var big = ordered[0].Key;                                  // the most shared secondary tileset
         var small = ordered.First(pair => pair.Value.Count >= 2 && pair.Value.Count < ordered[0].Value.Count && !SamePalette(hacked.RawData, pair.Key, big)).Key;
         var layoutToChange = uses[small][0];
         Console.WriteLine($"tileset {small:X6} ({uses[small].Count} layouts) -> {big:X6} ({uses[big].Count} layouts); layout {layoutToChange:X6}");
         var smallPal = ReadPointer(cubeRom, small + 8); var smallTiles = ReadPointer(cubeRom, small + 4);
         var smallPalBytes = cubeRom.Skip(smallPal).Take(512).ToArray();
         var bigPalBytes = cubeRom.Skip(ReadPointer(cubeRom, big + 8)).Take(512).ToArray();

         // the author: this map now uses the other tileset
         var token = new ModelDelta();
         hacked.WritePointer(token, layoutToChange + 20, big);
         hacked.ObserveRunWritten(token, hacked.GetNextRun(layoutToChange));
         // ... and drew a block of that other tileset, and a block of the one the map used before
         var bigBlocks = ReadPointer(cubeRom, big + 12); var smallBlocks = ReadPointer(cubeRom, small + 12);
         var bigAttributes = ReadPointer(cubeRom, big + 16);
         for (int i = 0; i < 16; i++) token.ChangeData(hacked, bigBlocks + 16 * 5 + i, (byte)(0x30 + i));
         for (int i = 0; i < 16; i++) token.ChangeData(hacked, smallBlocks + 16 * 7 + i, (byte)(0x50 + i));
         token.ChangeData(hacked, bigAttributes + 4 * 5, 0x21);
         var userToml = CubeUpdateService.Export(singletons, hacked);

         var result = CubeUpdateService.Update(singletons, (byte[])hacked.RawData.Clone(), userToml, File.ReadAllBytes(newRom), File.ReadAllLines(newToml), known);
         Console.WriteLine(result.Report.ToString().Replace(Environment.NewLine, " | "));
         if (old) {
            // the tilesets live elsewhere in the new CUBE: find them again by the blocks they have (the new CUBE did not change these tilesets)
            Assert.NotEqual(CubeUpdateService.CurrentName, result.Report.DetectedVersion);
            var updated = CubeUpdateService.Load(singletons, result.Rom, result.Toml);
            var newLayouts = updated.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "data.maps.layouts");
            var changedIndex = Enumerable.Range(0, layoutCount).First(i => ReadPointer(hacked.RawData, layouts + i * 4) == layoutToChange);
            var otherIndex = Enumerable.Range(0, layoutCount).First(i => ReadPointer(hacked.RawData, layouts + i * 4) == uses[small][1]);
            var bigIndex = Enumerable.Range(0, layoutCount).First(i => ReadPointer(hacked.RawData, layouts + i * 4) == uses[big][0]);
            int newChanged = ReadPointer(result.Rom, newLayouts + changedIndex * 4), newOther = ReadPointer(result.Rom, newLayouts + otherIndex * 4), newBigUser = ReadPointer(result.Rom, newLayouts + bigIndex * 4);
            int newSmall = ReadPointer(result.Rom, newOther + 20), newBig = ReadPointer(result.Rom, newBigUser + 20);
            Assert.Equal(newBig, ReadPointer(result.Rom, newChanged + 20));          // the changed map uses the big tileset of the new CUBE
            Assert.NotEqual(newSmall, newBig);
            Assert.Equal(smallPalBytes, result.Rom.Skip(ReadPointer(result.Rom, newSmall + 8)).Take(512).ToArray()); // the small tileset is itself
            Assert.Equal(bigPalBytes, result.Rom.Skip(ReadPointer(result.Rom, newBig + 8)).Take(512).ToArray());
            Assert.Equal(Enumerable.Range(0x30, 16).Select(v => (byte)v).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, newBig + 12) + 16 * 5).Take(16).ToArray());
            Assert.Equal(Enumerable.Range(0x50, 16).Select(v => (byte)v).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, newSmall + 12) + 16 * 7).Take(16).ToArray());
            Assert.Equal(0x21, result.Rom[ReadPointer(result.Rom, newBig + 16) + 4 * 5]);
            Assert.Equal(cubeRom.Skip(bigBlocks).Take(16 * 5).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, newBig + 12)).Take(16 * 5).ToArray());
            return;
         }

         // the small tileset is untouched: same tiles, same palette, same bytes
         Assert.Equal(smallTiles, ReadPointer(result.Rom, small + 4));
         Assert.Equal(smallPal, ReadPointer(result.Rom, small + 8));
         Assert.Equal(smallPalBytes, result.Rom.Skip(smallPal).Take(512).ToArray());
         foreach (var layout in uses[small].Skip(1)) Assert.Equal(small, ReadPointer(result.Rom, layout + 20));
         // the changed map uses the big tileset (the one of the new CUBE, not a copy)
         var now = ReadPointer(result.Rom, layoutToChange + 20);
         Assert.Equal(big, now);
         Assert.Equal(bigPalBytes, result.Rom.Skip(ReadPointer(result.Rom, big + 8)).Take(512).ToArray());
         Assert.Equal(0, result.Report.Copies);
         // the drawn blocks are there
         Assert.Equal(Enumerable.Range(0x30, 16).Select(v => (byte)v).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, big + 12) + 16 * 5).Take(16).ToArray());
         Assert.Equal(Enumerable.Range(0x50, 16).Select(v => (byte)v).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, small + 12) + 16 * 7).Take(16).ToArray());
         Assert.Equal(0x21, result.Rom[ReadPointer(result.Rom, big + 16) + 4 * 5]);
         // and nothing else of the two blocksets changed
         Assert.Equal(cubeRom.Skip(bigBlocks).Take(16 * 5).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, big + 12)).Take(16 * 5).ToArray());
         Assert.Equal(cubeRom.Skip(smallBlocks).Take(16 * 7).ToArray(), result.Rom.Skip(ReadPointer(result.Rom, small + 12)).Take(16 * 7).ToArray());
      }

      private static bool SamePalette(byte[] data, int a, int b) {
         int pa = ReadPointer(data, a + 8), pb = ReadPointer(data, b + 8);
         return pa == pb || data.Skip(pa).Take(512).SequenceEqual(data.Skip(pb).Take(512));
      }
   }
}
