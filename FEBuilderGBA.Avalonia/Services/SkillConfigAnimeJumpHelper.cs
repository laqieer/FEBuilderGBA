// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared "Jump to Animation Creator" seam (#1115) for the 4 anime-capable
    /// SkillConfig editors (SkillSystem, FE8N Ver2, FE8N Ver3, CSkillSys 0.9x).
    /// Mirrors the magic-jump pattern (`ImageMagicFEditorView.JumpEditor_Click`):
    /// PROBE-BEFORE-OPEN, then seed the Creator read-only via
    /// <see cref="ToolAnimationCreatorViewViewModel.InitFromSkillRom"/>.
    ///
    /// <para>FE8N Ver1 does NOT use this — it is render-only with no animation
    /// pointer in both WinForms and Avalonia (its jump shows an honest
    /// render-only message instead).</para>
    /// </summary>
    public static class SkillConfigAnimeJumpHelper
    {
        /// <summary>
        /// Probe the skill anime at <paramref name="animationPointer"/> and, when it
        /// has at least one frame, open the Animation Creator seeded from it. On a
        /// 0 / out-of-ROM / empty pointer, show an honest message and open NO window
        /// (never a blank Creator). All faults are caught + logged; never throws.
        /// </summary>
        /// <param name="selectedId">Skill id (window-title + frame-label hint).</param>
        /// <param name="animationPointer">The SkillConfig editor's resolved per-skill
        /// animation pointer (GBA pointer or raw offset).</param>
        /// <param name="viewLabel">Caller view name for log context.</param>
        public static void JumpToCreator(uint selectedId, uint animationPointer, string viewLabel)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null)
                {
                    CoreState.Services?.ShowInfo(R._("No ROM loaded."));
                    return;
                }
                if (animationPointer == 0)
                {
                    CoreState.Services?.ShowInfo(R._("No animation is set for this skill."));
                    return;
                }
                uint off = U.toOffset(animationPointer);
                if (!U.isSafetyOffset(off, rom))
                {
                    CoreState.Services?.ShowInfo(
                        R._("Animation pointer 0x{0:X} is outside the ROM.", animationPointer));
                    return;
                }

                // PROBE FIRST — do NOT open a blank Creator on an empty / unresolvable
                // / unrenderable skill-anime stream (#1115; Copilot PR #1137 review).
                // Use the SAME success condition the seed requires: a FULL
                // ExportSkillAnimation decode with no error AND >= 1 frame. The cheap
                // CountSkillFrames probe only validates the list pointers, not each
                // frame's resolved OBJ/TSA/palette offset — a config with a valid frame
                // stream but a bad per-frame resource would pass it yet seed empty. The
                // probe images are disposed here (the Creator's preview cache re-decodes
                // the single selected frame lazily), so this never leaks.
                var probe = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animationPointer);
                try
                {
                    if (!string.IsNullOrEmpty(probe.Error) || probe.Frames.Count == 0)
                    {
                        CoreState.Services?.ShowInfo(
                            R._("No renderable animation frames found for this skill at 0x{0:X}.", animationPointer));
                        return;
                    }
                }
                finally
                {
                    // Dispose the probe-decoded IImages (the export caches one per OBJ
                    // id, so dedup by reference before disposing).
                    var seen = new System.Collections.Generic.HashSet<IImage>(
                        System.Collections.Generic.ReferenceEqualityComparer.Instance);
                    foreach (var f in probe.Frames)
                        if (f.Image != null && seen.Add(f.Image))
                            try { f.Image.Dispose(); } catch { /* best-effort */ }
                }

                string hint = R._("Skill Animation #{0:X2}", selectedId);
                var view = WindowManager.Instance.Open<ToolAnimationCreatorView>();
                view.InitFromSkillRom(AnimationTypeEnum.Skill, selectedId, hint, animationPointer);
            }
            catch (Exception ex)
            {
                // Core Log.Error is params string[] (no composite formatting) — use a
                // single interpolated string with the full exception (#969 precedent).
                Log.Error($"{viewLabel}.JumpToEditor: {ex}");
            }
        }
    }
}
