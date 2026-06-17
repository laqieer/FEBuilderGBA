using System;
using System.Collections;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia tests for the All-Work-Support tool (#1196). Seeds a temp
    /// <c>config/etc/.../worksupport_.txt</c> tree, points
    /// <c>CoreState.BaseDirectory</c> at it, and verifies the tile ItemsControl
    /// populates (and stays empty when nothing is configured).
    /// </summary>
    [Collection("SharedState")]
    public class ToolAllWorkSupportViewHeadlessTests : IDisposable
    {
        readonly string _root;
        readonly string _savedBaseDir;
        readonly ROM _savedRom;

        public ToolAllWorkSupportViewHeadlessTests()
        {
            _savedBaseDir = CoreState.BaseDirectory;
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;
            _root = Path.Combine(Path.GetTempPath(), "fe_aws_view_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            CoreState.BaseDirectory = _savedBaseDir;
            CoreState.ROM = _savedRom;
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* best effort */ }
        }

        void SeedProject(string name)
        {
            string projDir = Path.Combine(_root, "projects");
            // One worksupport_.txt per project, each in its own etc subdir
            // (the scanner recurses; same-name files in one dir would collide).
            string etcDir = Path.Combine(_root, "config", "etc", name);
            Directory.CreateDirectory(projDir);
            Directory.CreateDirectory(etcDir);

            string rom = Path.Combine(projDir, name + ".gba");
            File.WriteAllBytes(rom, new byte[16]);
            File.WriteAllText(Path.Combine(etcDir, "worksupport_.txt"), "0\t" + rom + "\n");
            File.WriteAllText(Path.ChangeExtension(rom, ".updateinfo.txt"), "NAME=" + name + "\n");
        }

        static ItemsControl TilesList(Control view)
        {
            return view.GetLogicalDescendants().OfType<ItemsControl>()
                .First(c => AutomationProperties.GetAutomationId(c) == "ToolAllWorkSupport_Tiles_List");
        }

        static int ItemCount(ItemsControl ic)
        {
            if (ic.ItemsSource is IEnumerable e)
            {
                return e.Cast<object>().Count();
            }
            return ic.Items.Count;
        }

        [AvaloniaFact]
        public void View_CanInstantiate()
        {
            var view = new ToolAllWorkSupportView();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void View_WithSeededProjects_PopulatesTiles()
        {
            CoreState.BaseDirectory = _root;
            SeedProject("HackA");
            SeedProject("HackB");

            var view = new ToolAllWorkSupportView();
            view.Show();
            view.Hide();

            var ic = TilesList(view);
            Assert.Equal(2, ItemCount(ic));
        }

        [AvaloniaFact]
        public void View_EmptyConfig_RendersNoTiles_NoThrow()
        {
            CoreState.BaseDirectory = _root; // exists but no worksupport files
            Directory.CreateDirectory(Path.Combine(_root, "config", "etc"));

            var ex = Record.Exception(() =>
            {
                var view = new ToolAllWorkSupportView();
                view.Show();
                view.Hide();

                var ic = TilesList(view);
                Assert.Equal(0, ItemCount(ic));
            });
            Assert.Null(ex);
        }

        [AvaloniaFact]
        public void View_HasUpdateCheckButton()
        {
            CoreState.BaseDirectory = _root;
            Directory.CreateDirectory(Path.Combine(_root, "config", "etc"));

            var view = new ToolAllWorkSupportView();
            view.Show();
            view.Hide();

            var ids = view.GetLogicalDescendants().OfType<Control>()
                .Select(AutomationProperties.GetAutomationId)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            Assert.Contains("ToolAllWorkSupport_Tiles_List", ids);
            Assert.Contains("ToolAllWorkSupport_UpdateCheck_Button", ids);
        }
    }
}
