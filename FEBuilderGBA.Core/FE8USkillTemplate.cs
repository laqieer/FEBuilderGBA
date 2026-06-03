// SPDX-License-Identifier: GPL-3.0-or-later
// GUARD E (#917): the SINGLE shared source of truth for the FE8U per-skill
// SkillSystems animation program templates (`config/patch2/FE8U/skill/
// skillanimtemplate*.dmp`). Both the EXPORT seam (SkillSystemsAnimeExportCore.
// SkipCode — which SKIPS the prepended template when resolving the anime
// config) and the IMPORT seam (SkillSystemsAnimeImportCore — which RE-EMITS the
// template as the program-code prefix) reference these names + this directory.
// Keeping them in one place makes it structurally impossible for the two seams
// to disagree about which file / order is the defender variant.

using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Shared constants + path/selection helpers for the FE8U skill-animation
    /// program templates. Mirrors WF
    /// <c>ImageUtilSkillSystemsAnimeCreator.Import :589-598</c> (defender →
    /// <c>skillanimtemplate_defender_2017_01_24.dmp</c>, else
    /// <c>skillanimtemplate_2016_11_04.dmp</c>) and the WF
    /// <c>Path.Combine(BaseDirectory, "config", "patch2", "FE8U", "skill", ...)</c>
    /// directory.
    /// </summary>
    internal static class FE8USkillTemplate
    {
        /// <summary>The non-defender (attack) program template filename.</summary>
        internal const string AttackTemplate = "skillanimtemplate_2016_11_04.dmp";

        /// <summary>The defender program template filename.</summary>
        internal const string DefenderTemplate = "skillanimtemplate_defender_2017_01_24.dmp";

        /// <summary>
        /// Both templates, NON-DEFENDER FIRST. The export prefix-compare walks
        /// this order; the defender flag is derived from the matched filename.
        /// </summary>
        internal static readonly string[] TemplateFiles = new string[]
        {
            AttackTemplate,
            DefenderTemplate,
        };

        /// <summary>
        /// Absolute path to a template by filename:
        /// <c>{BaseDirectory}/config/patch2/FE8U/skill/{name}</c>.
        /// </summary>
        internal static string PathFor(string name)
            => Path.Combine(
                CoreState.BaseDirectory ?? "", "config", "patch2", "FE8U", "skill", name);

        /// <summary>
        /// Select the program-template filename for an import: the defender
        /// template when <paramref name="isDefender"/>, else the attack template
        /// (mirrors WF <c>animeType == AnimeType.D</c> branch :589-597).
        /// </summary>
        internal static string FileFor(bool isDefender)
            => isDefender ? DefenderTemplate : AttackTemplate;

        /// <summary>Absolute path to the selected import template (defender/attack).</summary>
        internal static string PathFor(bool isDefender)
            => PathFor(FileFor(isDefender));
    }
}
