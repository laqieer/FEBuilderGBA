using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Flag-Name assignment tool — parity with WinForms <c>ToolFlagNameForm</c> (#1191).
    /// Lists every event flag (<see cref="CoreState.FlagCache"/> = <c>EtcCacheFLag</c>) with
    /// its current name and lets the user assign a custom human-readable name (Write) or
    /// revert to the shipped default (Delete). Custom names live in the FlagCache (persisted
    /// to <c>config/etc/flag_*.txt</c>); the shipped base names come from
    /// <see cref="EtcCacheFLag.LoadBaseFlagNames"/>.
    /// </summary>
    public class ToolFlagNameViewModel : ViewModelBase
    {
        uint _selectedFlag;
        bool _hasSelection;
        bool _isLoaded;
        string _flagName = "";
        bool _isCustom;
        Dictionary<uint, string> _baseFlags;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The real flag number of the current selection.</summary>
        public uint SelectedFlag { get => _selectedFlag; set => SetField(ref _selectedFlag, value); }
        public bool HasSelection { get => _hasSelection; set => SetField(ref _hasSelection, value); }
        /// <summary>The editable name for the selected flag.</summary>
        public string FlagName { get => _flagName; set => SetField(ref _flagName, value); }
        /// <summary>True when the current name differs from the shipped base (a user customization).</summary>
        public bool IsCustom { get => _isCustom; set => SetField(ref _isCustom, value); }

        // Flag 0 -> AddrResult.addr 0 trips AddrResult.isNULL(); encode addr = flag + 1
        // (the real flag number is also kept in AddrResult.tag). Decode with FlagFromAddr.
        public static uint AddrFromFlag(uint flag) => flag + 1;
        public static uint FlagFromAddr(uint addr) => addr - 1;

        // Mirrors internal U.ToHexString (not accessible from Avalonia): magnitude-padded
        // hex with NO "0x" prefix, exactly as the WinForms list renders flag addresses.
        static string ToHexString(uint a) =>
            a <= 0xff ? a.ToString("X02")
            : a <= 0xffff ? a.ToString("X04")
            : a <= 0xffffff ? a.ToString("X06")
            : a.ToString("X08");

        Dictionary<uint, string> BaseFlags => _baseFlags ??= EtcCacheFLag.LoadBaseFlagNames();

        string BaseNameOf(uint flag) => BaseFlags.TryGetValue(flag, out string b) ? (b ?? "") : "";

        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            EtcCacheFLag cache = CoreState.FlagCache;
            if (cache == null) return result;

            foreach (AddrResult ar in cache.MakeList())
            {
                // Match WinForms: U.ToHexString(addr) + " " + name (magnitude-padded hex, no "0x").
                string label = (ToHexString(ar.addr) + " " + (ar.name ?? "")).TrimEnd();
                result.Add(new AddrResult(AddrFromFlag(ar.addr), label, ar.addr));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            IsLoaded = true;
            EtcCacheFLag cache = CoreState.FlagCache;
            uint flag = FlagFromAddr(addr);
            SelectedFlag = flag;
            if (cache == null)
            {
                HasSelection = false; FlagName = ""; IsCustom = false;
                return;
            }
            HasSelection = true;
            cache.TryGetValue(flag, out string cur);
            FlagName = cur ?? "";
            IsCustom = FlagName != BaseNameOf(flag);
        }

        /// <summary>
        /// Persist <paramref name="name"/> for <paramref name="flag"/>. Mirrors WinForms
        /// WriteButton: a name equal to the base is a no-op (use <see cref="Delete"/> to
        /// revert a customization). Returns true if the cache changed.
        /// </summary>
        public bool Write(uint flag, string name)
        {
            EtcCacheFLag cache = CoreState.FlagCache;
            if (cache == null) return false;
            string baseName = BaseNameOf(flag);
            if (baseName == (name ?? "")) return false;   // == base -> nothing to store (WinForms parity)
            cache.Update(flag, name ?? "", baseName);     // in-memory only; persist via Save()
            return true;
        }

        /// <summary>Revert <paramref name="flag"/> to its shipped base name (removes the customization).</summary>
        public void Delete(uint flag)
        {
            EtcCacheFLag cache = CoreState.FlagCache;
            if (cache == null) return;
            string baseName = BaseNameOf(flag);
            cache.Update(flag, "", baseName);   // Update with "" removes the custom entry + restores base
            FlagName = baseName;
            IsCustom = false;
        }

        /// <summary>
        /// Persist the customization table to config/etc/flag_*.txt. WinForms saves the
        /// FlagCache only on ROM save; the Avalonia ROM-save flow does not, so this
        /// standalone tool persists immediately after a Write/Delete.
        /// </summary>
        public void Save()
        {
            EtcCacheFLag cache = CoreState.FlagCache;
            string romFile = CoreState.ROM?.Filename;
            if (cache != null && !string.IsNullOrEmpty(romFile))
                cache.Save(romFile);
        }
    }
}
