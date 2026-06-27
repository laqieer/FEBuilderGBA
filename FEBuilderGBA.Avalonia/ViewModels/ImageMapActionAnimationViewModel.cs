using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel backing <c>ImageMapActionAnimationView</c>. Declared
    /// <c>partial</c> so the Phase 4 navigation manifest can live in
    /// a sibling <c>.NavigationTargets.cs</c> file without dragging the
    /// <c>FEBuilderGBA.Avalonia.Views</c> namespace into this file
    /// (#433 gap-sweep parity raise — Copilot CLI plan-review pt 1).
    /// </summary>
    public partial class ImageMapActionAnimationViewModel : ViewModelBase, IDataVerifiable
    {
        public const uint SIZE = 8;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _animationPointer, _padding1, _padding2;

        // Phase 1 (#433) — new fields surfacing the WF-only labels.
        uint _readStartAddress;
        uint _readCount;
        uint _selectedId;
        string _comment = "";
        bool _isEmptyEntry;
        bool _isAnimationValid;
        uint _selectedFrame;
        bool _showZoomed = true;
        int _frameCount;
        string _binInfoText = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // D0: Animation data pointer
        public uint AnimationPointer { get => _animationPointer; set => SetField(ref _animationPointer, value); }
        // W4: Padding / reserved
        public uint Padding1 { get => _padding1; set => SetField(ref _padding1, value); }
        // W6: Padding / reserved
        public uint Padding2 { get => _padding2; set => SetField(ref _padding2, value); }

        // Read-config bar — mirrors WF panel3 (先頭アドレス / 読込数 / 再取得).
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint BlockSize => SIZE;

        // The selected row's id (0-based, mirrors WF AddressList.SelectedIndex).
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }

        // WF Comment textbox + label.
        public string Comment { get => _comment; set => SetField(ref _comment, value ?? ""); }

        // Mirrors WF AddressList_SelectedIndexChanged branches:
        //   - SelectedIndex <= 0 -> show NOTIFY_KeepEmpty, hide animation panel.
        //   - SelectedIndex >  0 -> show animation panel iff D0 resolves safely.
        public bool IsEmptyEntry { get => _isEmptyEntry; set => SetField(ref _isEmptyEntry, value); }
        public bool IsAnimationValid { get => _isAnimationValid; set => SetField(ref _isAnimationValid, value); }

        // Animation panel state.
        public uint SelectedFrame { get => _selectedFrame; set => SetField(ref _selectedFrame, value); }
        public bool ShowZoomed { get => _showZoomed; set => SetField(ref _showZoomed, value); }
        public int FrameCount { get => _frameCount; set => SetField(ref _frameCount, value); }
        public string BinInfoText { get => _binInfoText; set => SetField(ref _binInfoText, value ?? ""); }

        // Cache of the default-name lookup table built from
        // config/data/MapActionAnimation_ALL.txt — mirrors WF
        // ImageMapActionAnimationForm.GetNameDefaultName.
        static Dictionary<uint, string>? _defaultNameCache;
        static readonly object _defaultNameLock = new object();

        /// <summary>
        /// Read the default human-readable name for the given id from
        /// <c>config/data/MapActionAnimation_ALL.txt</c>. Cached on first
        /// load. Returns the empty string when the file is missing or the
        /// id is not listed.
        /// </summary>
        public static string LoadDefaultName(uint id)
        {
            if (_defaultNameCache == null)
            {
                lock (_defaultNameLock)
                {
                    if (_defaultNameCache == null)
                    {
                        _defaultNameCache = LoadDefaultNamesFromConfig();
                    }
                }
            }
            return _defaultNameCache!.TryGetValue(id, out var name) ? name : "";
        }

        static Dictionary<uint, string> LoadDefaultNamesFromConfig()
        {
            // Use U.ConfigDataFilename + U.LoadDicResource ONLY when
            // CoreState.ROM is non-null — those helpers internally call
            // `U.OtherLangLine(line, CoreState.ROM)` which NREs on a null
            // ROM, and the catch path in `U.LoadDicResource` then dereferences
            // `CoreState.Services.ShowError` which can also be null in
            // headless tests. Mirrors WinForms `GetNameDefaultName`
            // (which only runs after the WF main form is up and a ROM is
            // loaded). Copilot CLI inline review on PR #506.
            ROM rom = CoreState.ROM;
            if (rom != null && rom.RomInfo != null)
            {
                try
                {
                    string path = U.ConfigDataFilename("MapActionAnimation_");
                    if (System.IO.File.Exists(path))
                    {
                        return U.LoadDicResource(path);
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorF("ImageMapActionAnimationViewModel.LoadDefaultNamesFromConfig U-path failed: {0}", ex.Message);
                }
            }

            // Fallback: parse the file directly with a hand-rolled loop.
            // Used when CoreState.ROM is null (headless tests with no ROM
            // available) or when the U.* path raises for any other reason.
            var dic = new Dictionary<uint, string>();
            try
            {
                string baseDir = CoreState.BaseDirectory ?? AppContext.BaseDirectory;
                string path = System.IO.Path.Combine(baseDir, "config", "data", "MapActionAnimation_ALL.txt");
                if (!System.IO.File.Exists(path)) return dic;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string hex = line.Substring(0, eq).Trim();
                    string name = line.Substring(eq + 1).Trim();
                    if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint id))
                    {
                        dic[id] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMapActionAnimationViewModel.LoadDefaultNamesFromConfig fallback failed: {0}", ex.Message);
            }
            return dic;
        }

        /// <summary>
        /// Reset the static default-name cache. Used by tests that swap
        /// out <c>CoreState.BaseDirectory</c> between runs so subsequent
        /// reads pick up the new config root.
        /// </summary>
        internal static void ResetDefaultNameCache()
        {
            lock (_defaultNameLock)
            {
                _defaultNameCache = null;
            }
        }

        /// <summary>
        /// Find the map action animation pointer table by binary signature search,
        /// matching the WinForms FindAnimationPointer() approach.
        /// </summary>
        static uint FindAnimationPointer(ROM rom)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            // Only FE8 has this editor
            if (rom.RomInfo.version != 8) return U.NOT_FOUND;

            byte[] bin;
            if (rom.RomInfo.is_multibyte)
            {   // FE8J
                bin = new byte[] { 0x54, 0x3C, 0x08, 0x08, 0xEC, 0xE1, 0x03, 0x02,
                                   0xE8, 0xA4, 0x03, 0x02, 0x68, 0xA5, 0x03, 0x02,
                                   0xFF, 0xFF, 0x00, 0x00 };
            }
            else
            {   // FE8U
                bin = new byte[] { 0x14, 0x19, 0x08, 0x08, 0xF0, 0xE1, 0x03, 0x02,
                                   0xEC, 0xA4, 0x03, 0x02, 0x6C, 0xA5, 0x03, 0x02,
                                   0xFF, 0xFF, 0x00, 0x00 };
            }

            // Match WinForms: start search from compress_image_borderline_address
            // to avoid false positives and reduce scan cost
            uint startAddr = rom.RomInfo.compress_image_borderline_address;
            uint p = U.GrepEnd(rom.Data, bin, startAddr, 0, 4, 0, true);
            if (p == U.NOT_FOUND) return U.NOT_FOUND;

            p = p - (uint)bin.Length - 4;
            uint a = rom.u32(p);
            if (!U.isPointer(a)) return U.NOT_FOUND;
            return p;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint animeP = FindAnimationPointer(rom);
            if (animeP == U.NOT_FOUND) return new List<AddrResult>();

            uint baseAddr = rom.p32(animeP);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            // Expose the read-config so the bar widgets can populate.
            ReadStartAddress = baseAddr;

            var result = new List<AddrResult>();
            for (int i = 0; ; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + 4 > (uint)rom.Data.Length) break;
                uint a = rom.u32(addr);
                if (!U.isSafetyPointerOrNull(a)) break;

                // Prefer the user-saved comment from CoreState.CommentCache
                // (matches WinForms `InputFormRef.GetCommentSA(addr)` lookup
                // in `Init.makeNameAt`); fall back to the per-id default name
                // when no cached comment is set.
                string label = "";
                if (CoreState.CommentCache != null)
                {
                    label = CoreState.CommentCache.At(addr) ?? "";
                }
                if (label.Length == 0)
                {
                    label = LoadDefaultName((uint)i);
                }
                string name = label.Length > 0
                    ? $"0x{i:X02} {label}"
                    : $"0x{i:X02} Map Action Animation";
                result.Add(new AddrResult(addr, name, (uint)i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            // If LoadList hasn't run yet (deep-linked NavigateTo), populate
            // ReadStartAddress lazily by running the signature search once,
            // then derive SelectedId via the cheap O(1) helper. After
            // LoadList runs, subsequent LoadEntry calls hit the fast path.
            if (ReadStartAddress == 0)
            {
                uint animeP = FindAnimationPointer(rom);
                if (animeP != U.NOT_FOUND)
                {
                    ReadStartAddress = rom.p32(animeP);
                }
            }
            SelectedId = ComputeIdForAddress(addr);

            AnimationPointer = rom.u32(addr + 0);
            Padding1 = rom.u16(addr + 4);
            Padding2 = rom.u16(addr + 6);

            // Default the Comment to the default name when none is set
            // — mirrors WF AddressList_SelectedIndexChanged behavior.
            // First check CoreState.CommentCache for a user-saved comment
            // at this address (matches `Program.CommentCache.At(addr)` in
            // WF InputFormRef.UI_ReadUIToComment line 5395); fall back to
            // the per-id default name when no user comment is set.
            string saved = "";
            if (CoreState.CommentCache != null)
            {
                saved = CoreState.CommentCache.At(addr) ?? "";
            }
            Comment = saved.Length > 0 ? saved : LoadDefaultName(SelectedId);

            // Mirror WF NOTIFY_KeepEmpty: ID=0 is reserved as null data.
            IsEmptyEntry = SelectedId == 0;

            // Animation panel visible iff D0 resolves to a safe ROM offset.
            uint animePtrOffset = U.toOffset(AnimationPointer);
            IsAnimationValid = !IsEmptyEntry && U.isSafetyOffset(animePtrOffset, rom);

            // Reset frame to 0 so the UI doesn't keep stale preview state.
            SelectedFrame = 0;

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Given a row address, derive the id (0-based index into the
        /// pointer table). Returns 0 when the address falls outside the
        /// expected table window or when LoadList hasn't yet populated
        /// the cached ReadStartAddress. Cheap O(1) derivation — does NOT
        /// rescan the ROM (the previous version re-ran the GrepEnd
        /// signature search on every selection change — Copilot CLI
        /// inline review on PR #506).
        /// </summary>
        uint ComputeIdForAddress(uint addr)
        {
            uint baseAddr = ReadStartAddress;
            if (baseAddr == 0 || addr < baseAddr) return 0;
            return (addr - baseAddr) / SIZE;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, AnimationPointer);
            rom.write_u16(addr + 4, Padding1);
            rom.write_u16(addr + 6, Padding2);

            // Persist the Comment to CoreState.CommentCache so the next
            // LoadEntry reads it back. Mirrors WinForms `UI_WriteCommentToUI`
            // (InputFormRef.cs line 5373) which writes the Comment textbox
            // through `Program.CommentCache.Update(addr, info_object.Text)`.
            // Without this, user edits to the Comment textbox are silently
            // discarded after Write+Reload — Copilot CLI review on PR #506.
            if (CoreState.CommentCache != null)
            {
                CoreState.CommentCache.Update(addr, Comment ?? "");
            }
        }

        /// <summary>
        /// Recompute the BinInfo line for the selected frame using
        /// <c>ImageUtilMapActionAnimationCore</c>. Mirrors WF
        /// <c>ShowFrameUpDown_ValueChanged</c> which stashes the
        /// per-frame log text into the BinInfo readonly textbox.
        /// </summary>
        public string ComputeFrameInfo(uint frameIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !IsAnimationValid)
            {
                BinInfoText = "";
                return BinInfoText;
            }

            uint animePtr = U.toOffset(AnimationPointer);
            int count = ImageUtilMapActionAnimationCore.CountFrames(animePtr);
            FrameCount = count;
            BinInfoText = $"Frame {frameIndex}/{count} @ 0x{animePtr:X08}";
            return BinInfoText;
        }

        public int GetListCount()
        {
            return LoadList().Count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AnimationPointer"] = $"0x{AnimationPointer:X08}",
                ["Padding1"] = $"0x{Padding1:X04}",
                ["Padding2"] = $"0x{Padding2:X04}",
                ["Comment"] = Comment,
                ["SelectedId"] = $"0x{SelectedId:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@4"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@6"] = $"0x{rom.u16(a + 6):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["AnimationPointer"] = "u32@0",
            ["Padding1"] = "u16@4",
            ["Padding2"] = "u16@6",
        };

        // ----------------------------------------------------------------
        // Export / Import / Source-file tracking (#499)
        // ----------------------------------------------------------------

        /// <summary>
        /// Cache key used by <see cref="RememberSourcePath"/> /
        /// <see cref="TryGetSourcePath"/> so the editor can remember the
        /// last-imported .MapActionAnimation.txt path across reloads.
        /// Mirrors WinForms `Program.ResourceCache.Update("MapActionAnimation_" + id, path)`.
        /// </summary>
        string MakeResourceCacheKey() => "MapActionAnimation_" + U.ToHexString(SelectedId);

        /// <summary>
        /// Export the currently selected animation to a
        /// <c>.MapActionAnimation.txt</c> script + per-unique-frame PNG
        /// companions.
        /// </summary>
        /// <param name="path">Output .MapActionAnimation.txt path.</param>
        /// <returns>Empty on success, error string otherwise.</returns>
        public string ExportScript(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM not loaded.");
            if (!IsAnimationValid) return R._("No valid animation selected to export.");
            uint animeAddr = U.toOffset(AnimationPointer);
            return MapActionAnimationExportImportCore.ExportScript(
                rom, animeAddr, path, Comment ?? "");
        }

        /// <summary>
        /// Export the currently selected animation as an animated GIF.
        /// </summary>
        /// <param name="path">Output .gif path.</param>
        /// <returns>Empty on success, error string otherwise.</returns>
        public string ExportGif(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM not loaded.");
            if (!IsAnimationValid) return R._("No valid animation selected to export.");
            uint animeAddr = U.toOffset(AnimationPointer);
            return MapActionAnimationExportImportCore.ExportGif(rom, animeAddr, path);
        }

        /// <summary>
        /// Import a <c>.MapActionAnimation.txt</c> script over the currently
        /// selected pointer slot. Caller is responsible for wrapping in an
        /// <c>UndoService.Begin/Commit/Rollback</c> scope.
        /// </summary>
        /// <param name="path">Input .MapActionAnimation.txt path.</param>
        /// <param name="imageLoader">Callback that turns a PNG path into
        /// <c>(rgba, w, h)</c> or returns null if the file is missing.</param>
        /// <returns>Empty on success, error string otherwise.</returns>
        public string ImportScript(string path, Func<string, (byte[] rgba, int w, int h)?> imageLoader)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM not loaded.");
            if (CurrentAddr == 0) return R._("No entry selected.");
            if (IsEmptyEntry) return R._("ID 0 is reserved as null data.");
            if (imageLoader == null) return R._("Image loader is null.");

            // The pointer slot is the currently selected row address (frames
            // table address lives there as a GBA pointer — `LoadEntry` reads
            // it back as `AnimationPointer`).
            return MapActionAnimationExportImportCore.ImportScript(
                rom, CurrentAddr, path, imageLoader);
        }

        /// <summary>
        /// Remember the file path the user just imported so the
        /// <c>OpenSource</c> / <c>SelectSource</c> buttons can re-open it on
        /// next selection. Uses <c>CoreState.ResourceCache</c> as the WF
        /// editor does. Best effort — silently no-ops when the cache is
        /// missing or the cast fails.
        /// </summary>
        public void RememberSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (CoreState.ResourceCache is EtcCacheResource cache)
            {
                try { cache.Update(MakeResourceCacheKey(), path); }
                catch { /* non-fatal — cache is best effort */ }
            }
        }

        /// <summary>
        /// Try to look up the source path remembered by
        /// <see cref="RememberSourcePath"/>. Returns true with the path when
        /// available.
        /// </summary>
        public bool TryGetSourcePath(out string path)
        {
            path = "";
            if (CoreState.ResourceCache is EtcCacheResource cache
                && cache.TryGetValue(MakeResourceCacheKey(), out var p)
                && !string.IsNullOrEmpty(p))
            {
                path = p;
                return true;
            }
            return false;
        }

        // ----------------------------------------------------------------
        // List expansion (#501) — delegates to DataExpansionCore.ExpandTableTo
        // ----------------------------------------------------------------

        /// <summary>
        /// Grow the action-animation pointer table to <paramref name="newCount"/>
        /// rows. Mirrors WinForms <c>InputFormRef.OnAddressListExpandsEventHandler</c>
        /// + <c>InputFormRef.ExpandsArea(ExpandsFillOption.NO, ...)</c>. After
        /// <c>ExpandTableTo</c> relocates the table it now composes the
        /// all-reference (raw 32-bit + ARM-Thumb LDR literal-pool) repoint via
        /// <see cref="DataExpansionCore.RepointAllReferences"/> (#1025), mirroring
        /// the merged <c>WorldMapImageViewModel</c> / <c>EventMapChange</c> expand
        /// pattern — not just the canonical pointer slot.
        ///
        /// Uses the editor's <see cref="ReadCount"/> as the current row count
        /// (the action-anime predicate <c>U.isSafetyPointerOrNull</c> accepts
        /// row 0 as a reserved-null entry, so <c>EstimateEntryCount</c> would
        /// undercount — see <see cref="DataExpansionCore.ExpandTableTo"/> XML
        /// doc). Caller is responsible for wrapping in an
        /// <c>UndoService.Begin/Commit/Rollback</c> scope and passing the active
        /// <see cref="Undo.UndoData"/> so the all-reference repoint is recorded.
        /// </summary>
        /// <param name="newCount">Target row count (must be &gt;= current).</param>
        /// <param name="undo">Active undo buffer for the ambient scope (may be
        /// null — the helper then relies on whatever ambient scope the caller
        /// already opened).</param>
        /// <returns>Empty on success, error string otherwise.</returns>
        public string ExpandList(uint newCount, Undo.UndoData? undo)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM not loaded.");

            uint animeP = FindAnimationPointer(rom);
            if (animeP == U.NOT_FOUND)
                return R._("Map action animation table not found in this ROM.");

            // Use the editor's row count (ReadCount), not EstimateEntryCount.
            // Row 0 is the reserved-null entry and ReadCount includes it.
            if (newCount < ReadCount)
                return R._("New count ({0}) must be greater than or equal to current count ({1}).",
                    newCount, ReadCount);
            if (newCount == ReadCount)
                return ""; // no-op success

            // Capture the OLD base BEFORE ExpandTableTo moves the table.
            uint oldBase = rom.p32(animeP);

            var result = DataExpansionCore.ExpandTableTo(rom, animeP, SIZE, ReadCount, newCount);
            if (!result.Success)
                return result.Error ?? R._("Table expansion failed.");

            // Repoint EVERY raw 32-bit + ARM-Thumb LDR literal-pool reference to
            // the old base — not just the canonical pointer ExpandTableTo already
            // moved. RepointAllReferences returning 0 (clean ROM, no secondary
            // refs) is SUCCESS — do NOT roll back on 0. The canonical slot now
            // holds the new base and is therefore NOT re-matched (no double-write).
            //
            // Pass null so RepointAllReferences uses the View's ambient
            // UndoService scope rather than opening a second ROM.BeginUndoScope
            // (overwrite-style; would clear the ambient scope on dispose). The
            // caller always wraps ExpandList in UndoService.Begin, whose ambient
            // scope auto-tracks every rom.write_* RepointAllReferences performs.
            DataExpansionCore.RepointAllReferences(rom, oldBase, result.NewBaseAddress, null);  // #1025: also repoint raw + LDR-literal refs

            // Refresh the read-config from the new pointer base (NOT a ROM write).
            ReadStartAddress = result.NewBaseAddress;
            ReadCount = result.NewCount;
            return "";
        }
    }
}
