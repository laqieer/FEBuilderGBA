using System.IO;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolChangeProjectnameViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _currentName = string.Empty;
        string _newName = string.Empty;
        string _statusMessage = string.Empty;
        string _helpText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Current project name (read-only in UI).
        /// WinForms: CurrentName TextBoxEx (ReadOnly=true).
        /// </summary>
        public string CurrentName { get => _currentName; set => SetField(ref _currentName, value); }

        /// <summary>
        /// New project name entered by the user.
        /// WinForms: NewName TextBoxEx (editable).
        /// </summary>
        public string NewName { get => _newName; set => SetField(ref _newName, value); }

        /// <summary>
        /// Status/error message shown after rename attempt.
        /// </summary>
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Help text explaining what the rename does.
        /// WinForms: textBox1 (readonly multiline).
        /// </summary>
        public string HelpText { get => _helpText; set => SetField(ref _helpText, value); }

        public void Initialize()
        {
            HelpText = "Safely renames the project files.\nPast backup names will also be updated.";

            if (CoreState.ROM != null && !string.IsNullOrEmpty(CoreState.ROM.Filename))
            {
                CurrentName = Path.GetFileNameWithoutExtension(CoreState.ROM.Filename);
                NewName = CurrentName;
            }

            IsLoaded = true;
        }

        /// <summary>
        /// Map a Core <see cref="ProjectRenameCore.ValidateResult"/> to a
        /// user-facing message (mirrors the WinForms <c>R.ShowStopError</c>
        /// strings). Pure — used by both the GUI and tests.
        /// </summary>
        public static string DescribeValidate(ProjectRenameCore.ValidateResult result)
        {
            switch (result)
            {
                // Reuse the exact WinForms keys (already translated in en/ja/zh)
                // so the messages match ToolChangeProjectnameForm verbatim.
                case ProjectRenameCore.ValidateResult.ModifiedRom:
                    return R._("変更したデータが保存されていません。\r\n名前を変更する前に、データを保存してください。");
                case ProjectRenameCore.ValidateResult.VirtualRom:
                    return R._("仮想ROMの名前を変更することはできません。");
                case ProjectRenameCore.ValidateResult.BadFilename:
                    return R._("ファイル名として利用できない文字が含まれています");
                case ProjectRenameCore.ValidateResult.EmptyName:
                    return R._("Please enter a new project name.");
                case ProjectRenameCore.ValidateResult.SameName:
                    return R._("The new name is the same as the current name.");
                case ProjectRenameCore.ValidateResult.NoRomFilename:
                    return R._("No ROM is loaded.");
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Perform the project rename against the live ROM. Returns the new ROM
        /// path on success (caller reloads it), or null on validation/IO failure
        /// with <see cref="StatusMessage"/> set.
        /// </summary>
        /// <param name="fs">
        /// Filesystem to use; null = real disk. Tests inject a fake.
        /// </param>
        public string TryRename(ProjectRenameCore.IProjectRenameFileSystem fs = null)
        {
            ROM rom = CoreState.ROM;
            ProjectRenameCore.ValidateResult result =
                ProjectRenameCore.Validate(rom, CurrentName, NewName);
            if (result != ProjectRenameCore.ValidateResult.Ok)
            {
                StatusMessage = DescribeValidate(result);
                return null;
            }

            try
            {
                string newPath = ProjectRenameCore.Rename(
                    rom, CurrentName, NewName, fs, out result);
                if (newPath == null)
                {
                    StatusMessage = DescribeValidate(result);
                    return null;
                }
                return newPath;
            }
            catch (System.IO.IOException ee)
            {
                StatusMessage = ee.Message;
                Log.Error(ee.ToString());
                return null;
            }
            catch (System.Exception ee)
            {
                StatusMessage = ee.Message;
                Log.Error(ee.ToString());
                return null;
            }
        }
    }
}
