using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class ContentRepoSetupWizardWinFormsTests : IDisposable
    {
        readonly string _baseDir;
        readonly object _previousConfig;
        readonly string _previousBaseDir;
        readonly bool _previousIsCommandLine;
        readonly PropertyInfo _configProp;
        readonly PropertyInfo _baseDirProp;
        readonly PropertyInfo _isCommandLineProp;

        public ContentRepoSetupWizardWinFormsTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FEBuilderGBA_ContentRepoSetupWizardWinFormsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            var program = typeof(OptionForm).Assembly.GetType("FEBuilderGBA.Program")!;
            _configProp = program.GetProperty("Config")!;
            _baseDirProp = program.GetProperty("BaseDirectory")!;
            _isCommandLineProp = program.GetProperty("IsCommandLine")!;
            _previousConfig = _configProp.GetValue(null)!;
            _previousBaseDir = (string)_baseDirProp.GetValue(null)!;
            _previousIsCommandLine = (bool)_isCommandLineProp.GetValue(null)!;

            var cfg = new ConfigWinForms();
            cfg.Load(Path.Combine(_baseDir, "config.xml"));
            _configProp.GetSetMethod(true)!.Invoke(null, new object?[] { cfg });
            _baseDirProp.GetSetMethod(true)!.Invoke(null, new object?[] { _baseDir });
            _isCommandLineProp.GetSetMethod(true)!.Invoke(null, new object?[] { false });
            CoreState.Config = cfg;
            CoreState.BaseDirectory = _baseDir;
        }

        public void Dispose()
        {
            _configProp.GetSetMethod(true)!.Invoke(null, new object?[] { _previousConfig });
            _baseDirProp.GetSetMethod(true)!.Invoke(null, new object?[] { _previousBaseDir });
            _isCommandLineProp.GetSetMethod(true)!.Invoke(null, new object?[] { _previousIsCommandLine });
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
        }

        [Fact]
        public void ContentRepoSetupWizardForm_Constructs()
        {
            string? err = null;
            var t = new Thread(() =>
            {
                try
                {
                    using var form = new ContentRepoSetupWizardForm();
                    form.CreateControl();
                    Assert.NotNull(form.Controls.Find("RowsPanel", true));
                }
                catch (Exception ex) { err = ex.ToString(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "STA thread did not complete within the timeout.");
            Assert.True(err == null, err);
        }

        [Fact]
        public void AutoShowGating_RespectsCommandLineAndCoreDecision()
        {
            Assert.True(Program.ShouldAutoShowContentRepoSetupWizard());
            _isCommandLineProp.GetSetMethod(true)!.Invoke(null, new object?[] { true });
            Assert.False(Program.ShouldAutoShowContentRepoSetupWizard());
            _isCommandLineProp.GetSetMethod(true)!.Invoke(null, new object?[] { false });

            Program.Config[ContentRepoSetupCore.OptOutConfigKey] = "1";
            Assert.False(Program.ShouldAutoShowContentRepoSetupWizard());
        }

        [Fact]
        public void EnsurePatch2Subdirectories_NoLongerContainsInteractivePromptOrClone()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "FEBuilderGBA", "Program.cs"));
            int methodStart = source.IndexOf("static void EnsurePatch2Subdirectories", StringComparison.Ordinal);
            Assert.True(methodStart >= 0, "EnsurePatch2Subdirectories not found");
            int methodEnd = source.IndexOf("static bool CheckConfigDirectory", methodStart, StringComparison.Ordinal);
            Assert.True(methodEnd > methodStart, "Could not isolate EnsurePatch2Subdirectories body");
            string body = source.Substring(methodStart, methodEnd - methodStart);

            Assert.DoesNotContain("R.ShowQ", body);
            Assert.DoesNotContain("GitUtil.Clone", body);
            Assert.DoesNotContain("GitUtil.Update", body);
            Assert.Contains("Directory.CreateDirectory", body);
        }

        [Fact]
        public void MainFormUtil_AddsSeparateContentRepoMenuItemNextToToolWizard()
        {
            string? err = null;
            var t = new Thread(() =>
            {
                try
                {
                    using var form = new Form();
                    var menu = new MenuStrip { Name = "menuStrip1" };
                    var settings = new ToolStripMenuItem { Name = "OptionSettingToolStripMenuItem", Text = "Settings" };
                    settings.DropDownItems.Add(new ToolStripMenuItem { Name = "InitWizardToolStripMenuItem", Text = "初期設定ウィザード" });
                    menu.Items.Add(settings);
                    form.Controls.Add(menu);

                    MainFormUtil.InstallContentRepoSetupMenuItem(form);
                    var found = FindToolStripMenuItem(menu.Items, "ContentRepoSetupToolStripMenuItem");
                    Assert.NotNull(found);
                    Assert.Equal("Content Repositories…", found!.Text);
                    Assert.Equal(1, settings.DropDownItems.IndexOf(found));
                }
                catch (Exception ex) { err = ex.ToString(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "STA thread did not complete within the timeout.");
            Assert.True(err == null, err);
        }

        static ToolStripMenuItem? FindToolStripMenuItem(ToolStripItemCollection items, string name)
        {
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripMenuItem menuItem)
                {
                    if (menuItem.Name == name) return menuItem;
                    var child = FindToolStripMenuItem(menuItem.DropDownItems, name);
                    if (child != null) return child;
                }
            }
            return null;
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent ?? "";
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
