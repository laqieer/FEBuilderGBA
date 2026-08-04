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
        readonly Config _previousCoreConfig;
        readonly string _previousCoreBaseDirectory;
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
            _previousCoreConfig = CoreState.Config;
            _previousCoreBaseDirectory = CoreState.BaseDirectory;

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
            CoreState.Config = _previousCoreConfig;
            CoreState.BaseDirectory = _previousCoreBaseDirectory;
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

        // #2037: internal ctor(bool gitAvailable) — the public parameterless ctor now just delegates to
        // this (proven by ContentRepoWizardWinForms_PublicCtorDelegatesToInternalGitAvailableCtor below).
        // Driving gitAvailable directly lets these tests exercise both layouts deterministically without
        // depending on whether the test host actually has git on PATH.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ContentRepoSetupWizardForm_InternalCtor_GitAvailableFlag_ControlsManualPanelAndInitButtons(bool gitAvailable)
        {
            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(gitAvailable);
                form.Show();
                form.PerformLayout();

                var manualPanel = (Control)form.Controls.Find("ManualPanel", true)[0];
                Assert.Equal(!gitAvailable, manualPanel.Visible);

                foreach (var descriptor in ContentRepoSetupCore.Repos)
                {
                    var button = (Button)form.Controls.Find(descriptor.Id + "_Init", true)[0];
                    Assert.Equal(gitAvailable, button.Visible);
                }
            });
        }

        // Untouched empty config: constructing the wizard and closing it without editing anything must be
        // a TRUE no-op — no config key gets created/pinned for any of the 3 repos, so a floating default
        // stays floating (a later change to GitUtil.*DefaultUrl / ContentRepoSetupCore defaults still takes
        // effect for a user who never touched the wizard).
        [Fact]
        public void ContentRepoSetupWizardForm_UntouchedEmptyConfig_CloseIsTrueNoOp()
        {
            foreach (var descriptor in ContentRepoSetupCore.Repos)
                Assert.False(Program.Config.ContainsKey(descriptor.ConfigKey), $"{descriptor.ConfigKey} should be absent before the test");

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false);
                form.Show();
                form.Close();
            });

            foreach (var descriptor in ContentRepoSetupCore.Repos)
                Assert.False(Program.Config.ContainsKey(descriptor.ConfigKey), $"{descriptor.ConfigKey} should remain absent after an untouched close");
        }

        // Editing all 3 rows with surrounding whitespace and closing (without ever clicking Initialize)
        // must persist the TRIMMED value for every row.
        [Fact]
        public void ContentRepoSetupWizardForm_EditedWhitespaceUrls_PersistTrimmedOnCloseWithoutInit()
        {
            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false); // manual layout: no Initialize button to accidentally click
                form.Show();

                foreach (var descriptor in ContentRepoSetupCore.Repos)
                {
                    var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                    url.Text = "  https://example.invalid/" + descriptor.Id + "  ";
                }

                form.Close();
            });

            foreach (var descriptor in ContentRepoSetupCore.Repos)
                Assert.Equal("https://example.invalid/" + descriptor.Id, Program.Config.at(descriptor.ConfigKey, "<absent>"));
        }

        // Clearing a previously-saved custom URL back to blank must persist an EMPTY string (a floating
        // default, not the pre-existing custom value and not simply left alone) and the manual-instructions
        // panel must immediately reflect the fallback to descriptor.DefaultUrl.
        [Fact]
        public void ContentRepoSetupWizardForm_ClearCustomUrl_PersistsEmpty_AndManualInstructionsFallBackToDefault()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];
            const string custom = "https://example.invalid/pre-existing-custom-fork";
            Program.Config[descriptor.ConfigKey] = custom;

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false);
                form.Show();

                var manual = (TextBox)form.Controls.Find("ManualInstructionsTextBox", true)[0];
                Assert.Contains(custom, manual.Text);

                var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                Assert.Equal(custom, url.Text);
                url.Text = ""; // user clears the field

                // TextChanged is wired live — no Close()/PersistRow needed to see the manual fallback.
                Assert.DoesNotContain(custom, manual.Text);
                Assert.Contains(descriptor.DefaultUrl, manual.Text);

                form.Close();
            });

            Assert.Equal("", Program.Config.at(descriptor.ConfigKey, "<absent>"));
            Assert.True(Program.Config.ContainsKey(descriptor.ConfigKey), "an explicitly-cleared row must persist an empty string, not remove the key or leave the old custom value");
        }

        // The shared internal PersistRow routine — the same one InitUpdateButton_Click and OnFormClosing
        // use — drives an A -> B (persisted mid-session, baseline refreshed) -> A (edited back) -> close
        // flow. The final close must re-persist A: comparing against the row's ORIGINAL starting baseline
        // would wrongly treat "back to A" as a no-op and leave the stale B saved.
        [Fact]
        public void ContentRepoSetupWizardForm_SharedPersistenceRoutine_AToBToA_FinalCloseSavesA()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false); // gitAvailable:false — applyRemote is a no-op either way
                form.Show();

                var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                string a = url.Text; // the starting displayed baseline (the floating default, since config started empty)

                url.Text = "  https://example.invalid/B  ";
                bool changedToB = form.PersistRow(descriptor, applyRemote: false);
                Assert.True(changedToB, "A -> B should be a real change");
                Assert.Equal("https://example.invalid/B", Program.Config.at(descriptor.ConfigKey, "<absent>"));

                url.Text = a; // edited back to the original value
                form.Close(); // routes through OnFormClosing -> PersistRow for every row

                Assert.Equal(a, Program.Config.at(descriptor.ConfigKey, "<absent>"));
            });
        }

        [Fact]
        public void ContentRepoSetupWizardForm_UntouchedWhitespaceConfig_CloseDoesNotRewriteRawValue()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];
            const string raw = "  https://example.invalid/whitespace-custom  ";
            Program.Config[descriptor.ConfigKey] = raw;

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false);
                form.Show();

                var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                Assert.Equal(raw.Trim(), url.Text);
                form.Close();
            });

            Assert.Equal(raw, Program.Config.at(descriptor.ConfigKey, "<absent>"));
        }

        [Fact]
        public void ContentRepoSetupWizardForm_RemoteApply_IsChangedRowOnly_AndClearUsesDefault()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];
            var calls = new System.Collections.Generic.List<(string Path, string Url)>();

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(
                    gitAvailable: true,
                    isGitRepo: _ => true,
                    setSubmoduleRemote: (path, url) =>
                    {
                        calls.Add((path, url));
                        return true;
                    });
                form.Show();

                Assert.False(form.PersistRow(descriptor, applyRemote: true));
                Assert.Empty(calls);

                var urlBox = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                urlBox.Text = "  https://example.invalid/changed  ";
                Assert.True(form.PersistRow(descriptor, applyRemote: true));
                Assert.Single(calls);
                Assert.Equal("https://example.invalid/changed", calls[0].Url);

                urlBox.Text = "";
                Assert.True(form.PersistRow(descriptor, applyRemote: true));
                Assert.Equal(2, calls.Count);
                Assert.Equal(descriptor.DefaultUrl, calls[1].Url);
            });
        }

        [Fact]
        public void ContentRepoSetupWizardForm_RemoteApplyFailure_DoesNotBlockPersistence()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(
                    gitAvailable: true,
                    isGitRepo: _ => true,
                    setSubmoduleRemote: (_, _) => throw new InvalidOperationException("simulated"));
                form.Show();

                var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                url.Text = "https://example.invalid/persist-despite-remote-error";
                Assert.True(form.PersistRow(descriptor, applyRemote: true));
            });

            Assert.Equal(
                "https://example.invalid/persist-despite-remote-error",
                Program.Config.at(descriptor.ConfigKey, "<absent>"));
        }

        [Fact]
        public void ContentRepoSetupWizardForm_RemoteApplyFalse_DoesNotBlockPersistence()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(
                    gitAvailable: true,
                    isGitRepo: _ => true,
                    setSubmoduleRemote: (_, _) => false);
                form.Show();

                var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                url.Text = "https://example.invalid/persist-despite-remote-false";
                Assert.True(form.PersistRow(descriptor, applyRemote: true));
            });

            Assert.Equal(
                "https://example.invalid/persist-despite-remote-false",
                Program.Config.at(descriptor.ConfigKey, "<absent>"));
        }

        [Fact]
        public void ContentRepoSetupWizardForm_PaddedBaseline_NormalizesBeforeInitUse()
        {
            var descriptor = ContentRepoSetupCore.Repos[0];
            const string normalized = "https://example.invalid/normalized";
            Program.Config[descriptor.ConfigKey] = normalized;

            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false);
                form.Show();

                var url = (TextBox)form.Controls.Find(descriptor.Id + "_Url", true)[0];
                url.Text = "  " + normalized + "  ";
                Assert.False(form.PersistRow(descriptor, applyRemote: false));
                Assert.Equal(normalized, url.Text);
            });
        }

        [Fact]
        public void ContentRepoSetupWizardForm_OperationState_BlocksNestedActionsAndUserClose()
        {
            int runCalls = 0;
            RunSta(() =>
            {
                ContentRepoSetupWizardForm form = null;
                form = new ContentRepoSetupWizardForm(
                    gitAvailable: true,
                    isGitRepo: _ => false,
                    setSubmoduleRemote: (_, _) => true,
                    runInitUpdate: (owner, repoDir, url, displayName) =>
                    {
                        runCalls++;
                        Assert.False(form.Controls.Find("CloseButton", true)[0].Enabled);
                        Assert.False(form.Controls.Find("DontShowAgainButton", true)[0].Enabled);
                        foreach (var repo in ContentRepoSetupCore.Repos)
                            Assert.False(form.Controls.Find(repo.Id + "_Init", true)[0].Enabled);

                        form.Close();
                        Assert.False(form.IsDisposed);
                        Assert.Equal(DialogResult.None, form.DialogResult);
                        Assert.Equal(
                            "0",
                            Program.Config.at(ContentRepoSetupCore.OptOutConfigKey, "0"));

                        // Disabled buttons cannot re-enter the repository action.
                        var secondButton = (Button)form.Controls.Find(
                            ContentRepoSetupCore.Repos[1].Id + "_Init",
                            true)[0];
                        secondButton.PerformClick();
                        typeof(ContentRepoSetupWizardForm)
                            .GetMethod(
                                "InitUpdateButton_Click",
                                BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(form, new object[] { secondButton, EventArgs.Empty });
                        Assert.Equal(1, runCalls);
                        return new Patch2GitResult { Kind = Patch2GitResultKind.Success };
                    });
                using (form)
                {
                    form.Show();
                    var first = ContentRepoSetupCore.Repos[0];
                    var url = (TextBox)form.Controls.Find(first.Id + "_Url", true)[0];
                    url.Text = "https://example.invalid/busy-test";
                    ((Button)form.Controls.Find(first.Id + "_Init", true)[0]).PerformClick();

                    Assert.Equal(1, runCalls);
                    Assert.False(form.IsDisposed);
                    foreach (var repo in ContentRepoSetupCore.Repos)
                        Assert.True(form.Controls.Find(repo.Id + "_Init", true)[0].Enabled);
                    Assert.True(form.Controls.Find("CloseButton", true)[0].Enabled);
                    Assert.True(form.Controls.Find("DontShowAgainButton", true)[0].Enabled);

                    form.Close();
                    Assert.True(form.IsDisposed);
                }
            });
        }

        [Fact]
        public void ContentRepoSetupWizardForm_ForcedCloseReason_IsNotCancelledWhileBusy()
        {
            RunSta(() =>
            {
                using var form = new ContentRepoSetupWizardForm(false);
                form.Show();
                form.SetOperationInProgress(true);

                var args = new FormClosingEventArgs(
                    CloseReason.WindowsShutDown,
                    cancel: false);
                typeof(ContentRepoSetupWizardForm)
                    .GetMethod(
                        "OnFormClosing",
                        BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(form, new object[] { args });

                Assert.False(args.Cancel);
                form.SetOperationInProgress(false);
            });
        }

        static void RunSta(Action body)
        {
            string? err = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { err = ex.ToString(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "STA thread did not complete within the timeout.");
            Assert.True(err == null, err);
        }

        // #2037: the public ctor must delegate production Git detection + remote operations into the
        // internal injectable constructor — proven from source so it can't silently regress into
        // duplicating the row-building logic or bypassing the test seams.
        [Fact]
        public void ContentRepoWizardWinForms_PublicCtorDelegatesProductionDependencies()
        {
            string root = FindRepoRoot();
            string form = File.ReadAllText(Path.Combine(root, "FEBuilderGBA", "ContentRepoSetupWizardForm.cs"));
            Assert.Contains("public ContentRepoSetupWizardForm()", form);
            Assert.Contains("ContentRepoSetupCore.IsGitAvailable(),", form);
            Assert.Contains("GitUtil.IsGitRepo,", form);
            Assert.Contains("GitUtil.SetSubmoduleRemote,", form);
            Assert.Contains("ContentRepoGitWinForms.RunInitUpdate)", form);
            Assert.Contains("internal ContentRepoSetupWizardForm(bool gitAvailable)", form);
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

        // #2036: display-name localization contract, proven from source so it cannot silently drift.
        // Deliberately a pure text contract: no global translation catalog is loaded here.
        [Fact]
        public void ContentRepoWizardWinForms_LocalizesDisplayNameLocally_AndPassesRawTokenToSharedHost()
        {
            string root = FindRepoRoot();
            string form = File.ReadAllText(Path.Combine(root, "FEBuilderGBA", "ContentRepoSetupWizardForm.cs"));

            // Row label + manual-instructions line: every render site localizes locally.
            Assert.Equal(2, Occurrences(form, "R._(descriptor.DisplayName)"));
            // The shared host receives the RAW descriptor token (it owns the single localization).
            Assert.Contains("_runInitUpdate(this, repoDir, effectiveUrl, descriptor.DisplayName)", form);
            Assert.DoesNotContain("_runInitUpdate(this, repoDir, effectiveUrl, R._(descriptor.DisplayName))", form);
            Assert.DoesNotContain("LoadTranslate", form);

            string host = File.ReadAllText(Path.Combine(root, "FEBuilderGBA", "ContentRepoGitWinForms.cs"));
            Assert.Equal(1, Occurrences(host, "R._(displayName)"));
            Assert.Contains("string.Format(R._(\"Git: {0} ...\"), localizedDisplayName)", host);
            Assert.Contains("R.ShowStopError(\"Another content repository operation is already running.\");", host);
            Assert.DoesNotContain("A content repository operation is already running.", host);
            Assert.DoesNotContain("LoadTranslate", host);

            string patch2Host = File.ReadAllText(Path.Combine(root, "FEBuilderGBA", "Patch2GitWinForms.cs"));
            Assert.Contains("ContentRepoGitWinForms.RunInitUpdate(owner, repoDir, url, \"Patch database\")", patch2Host);
            Assert.DoesNotContain("R._(", patch2Host);
        }

        static int Occurrences(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                count++;
            return count;
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
