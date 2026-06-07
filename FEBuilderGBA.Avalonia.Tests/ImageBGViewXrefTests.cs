// SPDX-License-Identifier: GPL-3.0-or-later
// Headless Avalonia regression test for the Background Image Editor References
// list (#990). Proves the ImageBGView XRefList binding projects a POPULATED
// list (not a permanent empty stub) for a runtime-discovered referenced BG id.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Loads a real ROM, wires CoreState.EventScript, discovers a referenced BG
    /// id via the Core BGReferenceFinder, selects that BG slot in ImageBGView,
    /// and asserts XRefList.ItemsSource is non-null, its count matches the VM's
    /// XRefEntries, and is > 0 for the discovered id.
    /// </summary>
    [Collection("SharedState")]
    public class ImageBGViewXrefTests
    {
        const uint SIZE = 12; // ImageBGViewModel.SIZE

        [AvaloniaFact]
        public void XRefList_PopulatedForReferencedBg()
        {
            string romPath = FindRom("FE8U.gba") ?? FindRom("FE7U.gba") ?? FindRom("FE6.gba");
            if (romPath == null) return; // skip when no ROM present

            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            var savedSvc = CoreState.ImageService;
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                CoreState.BaseDirectory = asmDir;

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                if (CoreState.CommentCache == null)
                    CoreState.CommentCache = new HeadlessEtcCache();
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();

                var es = new EventScript();
                es.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = es;

                BGReferenceFinder.ResetCache();

                // BG table base (so we can map a bgId -> table-row address).
                uint ptr = rom.RomInfo.bg_pointer;
                Assert.NotEqual(0u, ptr);
                uint baseAddr = rom.p32(ptr);
                Assert.True(U.isSafetyOffset(baseAddr, rom));

                // Show the view (Opened -> LoadList populates EntryList).
                var view = new ImageBGView();
                view.Show();
                Dispatcher.UIThread.RunJobs();

                var entryList = view.FindControl<FEBuilderGBA.Avalonia.Controls.AddressListControl>("EntryList");
                Assert.NotNull(entryList);
                var items = entryList!.GetItems();
                Assert.NotEmpty(items);

                // Find the FIRST list row (a valid BG table slot) whose bgId is
                // referenced by an event script. The row tag == bgId == row index.
                uint? targetAddr = null;
                int expectedRefCount = 0;
                foreach (var item in items)
                {
                    uint bgId = item.tag;
                    var refs = BGReferenceFinder.MakeListByUseBG(rom, bgId);
                    if (refs.Count > 0)
                    {
                        targetAddr = item.addr;
                        expectedRefCount = refs.Count;
                        break;
                    }
                }

                Assert.True(targetAddr.HasValue,
                    "Expected at least one listed BG slot to be referenced by an event script in a vanilla ROM");

                // Drive the user-equivalent selection path.
                entryList.SelectAddress(targetAddr!.Value);
                Dispatcher.UIThread.RunJobs();

                var xrefList = view.FindControl<ListBox>("XRefList");
                Assert.NotNull(xrefList);
                Assert.NotNull(xrefList!.ItemsSource);

                int listCount = CountItems(xrefList.ItemsSource);
                Assert.True(listCount > 0,
                    "References list must be populated for the discovered referenced BG id");

                // Count must equal the VM's XRefEntries (the binding projects the
                // VM list 1:1).
                var vm = GetViewModel(view);
                Assert.NotNull(vm);
                var xrefEntries = (System.Collections.IList)vm!.GetType()
                    .GetProperty("XRefEntries")!.GetValue(vm)!;
                Assert.Equal(xrefEntries.Count, listCount);
                Assert.Equal(expectedRefCount, listCount);

                view.Close();
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                CoreState.ImageService = savedSvc;
                if (savedBaseDir != null)
                    CoreState.BaseDirectory = savedBaseDir;
                BGReferenceFinder.ResetCache();
            }
        }

        static int CountItems(IEnumerable source)
        {
            int n = 0;
            foreach (var _ in source) n++;
            return n;
        }

        static object GetViewModel(ImageBGView view)
        {
            var f = typeof(ImageBGView).GetField("_vm",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(view);
        }

        static string FindRom(string romName)
        {
            // Reuse TestRomLocator's resolved roms/ dir.
            string dir = TestRomLocator.RomsDir;
            if (dir == null) return null;
            string path = Path.Combine(dir, romName);
            return File.Exists(path) ? path : null;
        }
    }
}
