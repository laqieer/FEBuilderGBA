using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Add-via-ASM/C "Make Patch" text dialog
    /// (<c>AsmPatchTextView</c>) — the Avalonia port of the shared WinForms
    /// <c>GraphicsToolPatchMakerForm</c>. It just holds the already-generated
    /// redistributable patch-definition text (built by
    /// <see cref="AsmCompileCore.MakePatchText"/>) for display + save; it reads NO ROM
    /// bytes and performs no data verification, so it is a plain display VM (no
    /// data-verification contract).
    /// </summary>
    public class AsmPatchTextViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _patchText = "";
        string _statusMessage = "";

        /// <summary>True once the dialog has been initialized.</summary>
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>The generated patch-definition text shown read-only and saved as-is.</summary>
        public string PatchText { get => _patchText; set => SetField(ref _patchText, value); }

        /// <summary>Result / error text shown under the buttons.</summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>Load the patch text to display (WF <c>GraphicsToolPatchMakerForm.Init</c>).</summary>
        public void Load(string patchText)
        {
            PatchText = patchText ?? "";
            IsLoaded = true;
        }

        /// <summary>
        /// Save the patch text to <paramref name="outputPath"/>, substituting the
        /// <c>&lt;&lt;PATCH NAME&gt;&gt;</c> placeholder with the patch name derived from
        /// the chosen file name (WF <c>SaveButton_Click</c>): the file stem with a
        /// leading <c>PATCH_</c> stripped. Returns true on success; on a write failure
        /// it sets <see cref="StatusMessage"/> and returns false (never throws — the
        /// caller already wraps this, but the VM stays fault-safe on its own).
        /// </summary>
        public bool Save(string outputPath) => Save(outputPath, outputPath);

        /// <summary>
        /// #1639 SAF overload: write the patch to <paramref name="outputPath"/>
        /// (which may be a temp file on Android) while deriving the patch NAME
        /// from <paramref name="nameSourceFileName"/> — the file name the user
        /// actually chose. On desktop both arguments are the same path.
        /// </summary>
        public bool Save(string outputPath, string nameSourceFileName)
        {
            if (string.IsNullOrEmpty(outputPath)) return false;

            // Derive the patch name from the file the user picked. WF strips a leading
            // "PATCH_" from the stem so "PATCH_MyHack.txt" → "MyHack"; a file without
            // that prefix keeps its full stem.
            string stem = Path.GetFileNameWithoutExtension(nameSourceFileName);
            string name = stem.StartsWith("PATCH_", System.StringComparison.Ordinal)
                ? stem.Substring("PATCH_".Length)
                : stem;

            string text = PatchText.Replace("<<PATCH NAME>>", name);

            try
            {
                File.WriteAllText(outputPath, text);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                StatusMessage = R._("Failed to save the patch file.") + "\r\n" + ex.Message;
                return false;
            }

            StatusMessage = R._("Saved to {0}", outputPath);
            return true;
        }
    }
}
