using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.CubeUpdate;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   /// <summary>A ROM that already is the current CUBE is updated too: the edits are moved onto a fresh copy and the .toml is written anew.</summary>
   public class UpdateCubeRefreshTests {
      [SkippableFact]
      public void CurrentCube_WithAnOldToml_IsRefreshed_AndKeepsItsEdits() {
         var rom = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE"); var toml = Environment.GetEnvironmentVariable("HMA_INTRO_CUBE_TOML");
         var fp = Environment.GetEnvironmentVariable("UPD_FP");
         Skip.If(string.IsNullOrEmpty(rom) || !File.Exists(rom) || string.IsNullOrEmpty(fp) || !File.Exists(fp), "set HMA_INTRO_CUBE, HMA_INTRO_CUBE_TOML and UPD_FP (cube_versions.fp)");
         var singletons = BaseViewModelTestClass.Singletons;
         var cubeRom = File.ReadAllBytes(rom); var cubeToml = File.ReadAllLines(toml);
         var known = CubeFingerprint.Read(File.ReadAllBytes(fp));

         // the author's ROM: the current CUBE with two edits, and a .toml that an older program saved (a list with a hole in it)
         var hacked = CubeUpdateService.Load(singletons, (byte[])cubeRom.Clone(), cubeToml);
         var token = new ModelDelta();
         var moves = new ModelTable(hacked, hacked.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "data.pokemon.moves.stats"), () => token);
         moves[10].SetValue("pp", 37);
         moves[11].SetStringValue("name", "MY OWN MOVE NAME");
         // ... and a script of their own, named, that the first map's first object event runs
         var scriptAddress = hacked.FindFreeSpace(0x100, 0x20);
         var scriptBytes = new byte[] { 0x6A, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x06, 0x6C, 0x02 }; // lock, loadpointer 0 <0>, callstd 6, release, end
         for (int i = 0; i < scriptBytes.Length; i++) token.ChangeData(hacked, scriptAddress + i, scriptBytes[i]);
         hacked.ObserveAnchorWritten(token, "scripts.mine.hello", new HavenSoft.HexManiac.Core.Models.Runs.XSERun(scriptAddress));
         var banks = hacked.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "data.maps.banks");
         var firstMap = hacked.ReadPointer(hacked.ReadPointer(banks));
         var events = hacked.ReadPointer(firstMap + 4);
         var objects = hacked.ReadPointer(events + 4);
         Assert.True(hacked[events] > 0, "the first map has object events");
         hacked.WritePointer(token, objects + 16, scriptAddress);
         var userToml = CubeUpdateService.Export(singletons, hacked).ToList();
         var at = userToml.FindIndex(line => line.Contains("Name = '''damagecategory'''"));
         Assert.True(at > 0);
         var end = userToml.FindIndex(at, line => line.Trim() == "]");
         userToml.Insert(end + 1, "6 = '''late'''"); // entries 4 and 5 are holes

         var result = CubeUpdateService.Update(singletons, (byte[])hacked.RawData.Clone(), userToml.ToArray(), cubeRom, cubeToml, known);
         Console.WriteLine(result.Report.ToString().Replace(Environment.NewLine, " | "));
         Assert.Equal(CubeUpdateService.CurrentName, result.Report.DetectedVersion);
         Assert.True(result.Report.Notes.Any(note => note.Contains("fresh copy")));
         Assert.True(result.Report.NodesEdited <= 4, result.Report.NodesEdited + " edits found");

         var updated = CubeUpdateService.Load(singletons, result.Rom, result.Toml);
         var table = new ModelTable(updated, updated.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "data.pokemon.moves.stats"), () => new ModelDelta());
         Assert.Equal(37, table[10].GetValue("pp"));
         Assert.Equal("MY OWN MOVE NAME", table[11].GetStringValue("name"));
         var newBanks = updated.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "data.maps.banks");
         var newObjects = updated.ReadPointer(updated.ReadPointer(updated.ReadPointer(updated.ReadPointer(newBanks)) + 4) + 4);
         var newScript = updated.ReadPointer(newObjects + 16);
         Assert.True(newScript > 0);
         Assert.Equal(scriptBytes, Enumerable.Range(0, scriptBytes.Length).Select(i => (byte)updated[newScript + i]).ToArray());
         Assert.Equal(newScript, updated.GetAddressFromAnchor(new NoDataChangeDeltaModel(), -1, "scripts.mine.hello")); // the name went with the script
         Assert.True(updated.TryGetList("damagecategory", out var list));
         Assert.Equal(4, list.Count); // the shipped list, not the one with holes
         Assert.True(CubeErrorScreen.IsDisabled(result.Rom, CubeErrorScreen.Find(result.Rom)) || CubeErrorScreen.Find(result.Rom) >= 0); // the service leaves the screen to the command
      }
   }
}
