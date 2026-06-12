// SPDX-License-Identifier: GPL-3.0-or-later
// ViewModel for ToolAnimationCreatorView — issue #500.
//
// Two Init paths:
//   - InitFromRom(...)  — direct from a ROM frame table (used by
//                          ImageMapActionAnimationView's "Open in Creator"
//                          button — no temp file).
//   - InitFromFile(...) — from a .txt script (parity with WF
//                          ToolAnimationCreatorForm.Init(filename) for future
//                          ports of Battle/Magic/Skill animation kinds).
//
// Both paths populate the same `Frames` collection of editable wrappers, so
// the view binds against one source of truth regardless of which path opened it.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Mutable per-frame view-model wrapper around the Core
    /// <see cref="MapActionFrame"/> record so AXAML bindings can `{Binding
    /// Wait}` etc. and the UI updates as the user types.
    /// </summary>
    public class EditableMapActionFrame : ViewModelBase
    {
        uint _wait;
        uint _sound;
        uint _imagePointer;
        uint _palettePointer;
        string _imageName = string.Empty;

        public EditableMapActionFrame() { }

        public EditableMapActionFrame(MapActionFrame src)
        {
            _wait = src.Wait;
            _sound = src.Sound;
            _imagePointer = src.ImagePointer;
            _palettePointer = src.PalettePointer;
            _imageName = src.ImageName ?? string.Empty;
        }

        public uint Wait { get => _wait; set => SetField(ref _wait, value); }
        public uint Sound { get => _sound; set => SetField(ref _sound, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public string ImageName { get => _imageName; set => SetField(ref _imageName, value ?? string.Empty); }

        /// <summary>Project back to the immutable Core record.</summary>
        public MapActionFrame ToRecord()
            => new MapActionFrame(_wait, _imagePointer, _palettePointer, _sound,
                string.IsNullOrEmpty(_imageName) ? null : _imageName);
    }

    public class ToolAnimationCreatorViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _animationName = string.Empty;
        string _frameCount = string.Empty;
        string _imageSource = string.Empty;
        string _fileHint = string.Empty;
        string? _sourceFilename;
        uint _romAddress;
        uint _magicFrameDataAddress;
        AnimationTypeEnum _animationKind = AnimationTypeEnum.MapActionAnimation;
        uint _animationId;
        EditableMapActionFrame? _selectedFrame;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AnimationName { get => _animationName; set => SetField(ref _animationName, value ?? string.Empty); }
        public string FrameCount { get => _frameCount; set => SetField(ref _frameCount, value ?? string.Empty); }
        public string ImageSource { get => _imageSource; set => SetField(ref _imageSource, value ?? string.Empty); }
        public string FileHint { get => _fileHint; set => SetField(ref _fileHint, value ?? string.Empty); }
        public string? SourceFilename { get => _sourceFilename; set => SetField(ref _sourceFilename, value); }
        public uint RomAddress { get => _romAddress; set => SetField(ref _romAddress, value); }
        /// <summary>
        /// Magic frame-data stream address (#996). DISPLAY/PREVIEW ONLY — magic
        /// seeds are read-only, so this is kept separate from
        /// <see cref="RomAddress"/> (which gates write-back). For magic seeds
        /// <see cref="RomAddress"/> stays 0 so pressing Create can NEVER overwrite
        /// the 0x86 magic stream with 12-byte MapAction rows.
        /// </summary>
        public uint MagicFrameDataAddress { get => _magicFrameDataAddress; set => SetField(ref _magicFrameDataAddress, value); }
        public AnimationTypeEnum AnimationKind { get => _animationKind; set => SetField(ref _animationKind, value); }
        public uint AnimationId { get => _animationId; set => SetField(ref _animationId, value); }

        public EditableMapActionFrame? SelectedFrame
        {
            get => _selectedFrame;
            set => SetField(ref _selectedFrame, value);
        }

        /// <summary>
        /// True when the VM was Init'd from a ROM and Write_Click can commit
        /// the edited frames back to that ROM range. False for from-file inits
        /// (those write a .txt instead) AND for magic seeds (#996) — write-back
        /// is MapAction-only because the 12-byte MapAction row format would
        /// corrupt the 0x86 magic frame stream.
        /// </summary>
        public bool CanWriteBackToRom =>
            _animationKind == AnimationTypeEnum.MapActionAnimation
            && _romAddress != 0 && CoreState.ROM != null;

        public ObservableCollection<EditableMapActionFrame> Frames { get; } = new();

        /// <summary>Standalone open (no Init) — preserves the pre-#500 placeholder behavior.</summary>
        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Init from a .txt script previously emitted by WF Export or by
        /// <see cref="ToolAnimationCreatorCore.WriteMapActionScript"/>.
        /// Copies all parsed state into VM fields so the source file can be
        /// safely deleted by the caller after Init returns (mirrors WF
        /// temp-directory lifetime — Copilot CLI plan-review pt 1).
        /// </summary>
        public void InitFromFile(AnimationTypeEnum kind, uint id, string filehint, string filename)
        {
            IsLoading = true;
            try
            {
                AnimationKind = kind;
                AnimationId = id;
                FileHint = filehint ?? string.Empty;
                RomAddress = 0; // file path — no ROM writeback
                Frames.Clear();
                SelectedFrame = null;

                // #996 fail-closed: the .txt parser only understands the 12-byte
                // MapAction frame format. Reject any other kind WITHOUT parsing so
                // we never load garbage rows. The VM stays loaded-but-empty.
                // SourceFilename is cleared (NOT set to filename) so Create_Click
                // reports "No source loaded." and Save is a no-op for rejected
                // kinds — otherwise a user could overwrite the source file with an
                // empty/invalid MapAction script (Copilot review on #1116).
                if (kind != AnimationTypeEnum.MapActionAnimation)
                {
                    SourceFilename = null;
                    AnimationName = filehint ?? string.Empty;
                    FrameCount = Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    IsLoaded = true;
                    return;
                }

                List<MapActionFrame>? parsed = null;
                string? name = null;
                try
                {
                    parsed = ToolAnimationCreatorCore.ParseMapActionScript(filename, out name);
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    Log.Error("ToolAnimationCreator.InitFromFile: file not found: {0}", ex.FileName ?? filename);
                    // Leave VM in a partially-initialised state: AnimationKind /
                    // AnimationId / FileHint are set but Frames is empty.
                    // IsLoaded still flips true so the standalone-open path is
                    // a no-op-but-not-an-error.
                }

                AnimationName = name ?? string.Empty;
                if (parsed != null)
                {
                    foreach (var f in parsed)
                        Frames.Add(new EditableMapActionFrame(f));
                }
                FrameCount = Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // SourceFilename retained so Create_Click can save back when
                // the file is still accessible. Callers that pass a temp path
                // should clean it up themselves after Init returns.
                SourceFilename = filename;
                IsLoaded = true;
            }
            finally { IsLoading = false; MarkClean(); }
        }

        /// <summary>
        /// Init directly from the ROM frame table. The "Open in Animation
        /// Creator" entry point on <c>ImageMapActionAnimationView</c> uses this
        /// path — no temp file involved.
        /// </summary>
        public void InitFromRom(AnimationTypeEnum kind, uint id, string filehint, uint romAddress)
        {
            IsLoading = true;
            try
            {
                AnimationKind = kind;
                AnimationId = id;
                FileHint = filehint ?? string.Empty;
                SourceFilename = null;
                Frames.Clear();
                SelectedFrame = null;

                if (kind == AnimationTypeEnum.MapActionAnimation)
                {
                    RomAddress = romAddress;

                    var rom = CoreState.ROM;
                    if (rom != null)
                    {
                        var fromRom = ToolAnimationCreatorCore.ReadFromRom(rom, romAddress);
                        foreach (var f in fromRom)
                            Frames.Add(new EditableMapActionFrame(f));
                    }
                }
                else
                {
                    // #996 fail-closed: ReadFromRom only understands the 12-byte
                    // MapAction frame format. For any other kind do NOT call it
                    // (it would read garbage). Leave RomAddress = 0 (no write-back)
                    // and Frames empty. Callers that want a populated magic seed
                    // use InitFromMagicRom instead.
                    RomAddress = 0;
                }

                AnimationName = filehint ?? string.Empty;
                FrameCount = Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                IsLoaded = true;
                // CanWriteBackToRom depends on RomAddress + CoreState.ROM + kind,
                // all of which we just set — fire the property-changed
                // notification so the view's binding picks it up.
                OnPropertyChanged(nameof(CanWriteBackToRom));
            }
            finally { IsLoading = false; MarkClean(); }
        }

        /// <summary>
        /// Seed the Creator from a MAGIC animation frame-data stream (#996) — the
        /// FEditor (28-byte stride) or CSA Creator (32-byte stride) 0x86 frame
        /// format. Used by the Magic editors' "Editor"/"Jump to Animation Creator"
        /// buttons so the Creator opens POPULATED rather than blank.
        ///
        /// <para><b>READ-ONLY display/preview.</b> Magic frames are NOT writable
        /// through this view's 12-byte MapAction writer, so <see cref="RomAddress"/>
        /// is forced to 0 (pressing Create is a no-op for write-back) and the magic
        /// stream address is stored in <see cref="MagicFrameDataAddress"/> for
        /// display/preview only.</para>
        /// </summary>
        /// <param name="kind">MagicAnime_FEEDitor or MagicAnime_CSACreator.</param>
        /// <param name="id">1-based magic-animation entry id (for the title hint).</param>
        /// <param name="filehint">Human-readable hint shown in the window title.</param>
        /// <param name="frameDataAddr">GBA pointer (or raw offset) to the magic
        /// 0x86 frame-data stream.</param>
        /// <param name="isCsa"><c>true</c> for CSA Creator (32-byte frame, +28 TSA);
        /// <c>false</c> for FEditor (28-byte frame).</param>
        public void InitFromMagicRom(AnimationTypeEnum kind, uint id, string filehint, uint frameDataAddr, bool isCsa)
        {
            IsLoading = true;
            try
            {
                AnimationKind = kind;
                AnimationId = id;
                FileHint = filehint ?? string.Empty;

                // CRITICAL write-back guard (#996): magic seeds are READ-ONLY.
                // RomAddress stays 0 so Create can NEVER overwrite the 0x86 magic
                // stream with 12-byte MapAction rows. The frame-data address is
                // kept separately for display/preview only.
                RomAddress = 0;
                MagicFrameDataAddress = frameDataAddr;
                SourceFilename = null;
                Frames.Clear();
                SelectedFrame = null;

                var rom = CoreState.ROM;
                if (rom != null)
                {
                    _ = MagicEffectExportCore.ExportMagicScriptLines(
                        rom, frameDataAddr, basename: "", enableComment: false,
                        out _, out _, out var frames, isCsa: isCsa);
                    foreach (MagicFrameMeta f in frames)
                    {
                        Frames.Add(new EditableMapActionFrame(new MapActionFrame(
                            Wait: f.Wait,
                            ImagePointer: f.ObjImageOffset,
                            PalettePointer: f.ObjPaletteOffset,
                            Sound: 0,
                            ImageName: null)));
                    }
                }

                AnimationName = filehint ?? string.Empty;
                FrameCount = Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                IsLoaded = true;
                OnPropertyChanged(nameof(CanWriteBackToRom));
            }
            finally { IsLoading = false; MarkClean(); }
        }

        /// <summary>
        /// Project the current editable frame list back into a
        /// <see cref="MapActionFrame"/> list for write-back. Used by both the
        /// ROM-write and file-write branches of <c>Create_Click</c>.
        /// </summary>
        public List<MapActionFrame> ProjectFrames()
        {
            var list = new List<MapActionFrame>(Frames.Count);
            foreach (var ef in Frames)
                list.Add(ef.ToRecord());
            return list;
        }
    }
}
