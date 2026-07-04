using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    // #1806 — the shared FE-Repo browse-button helper must place the button inside the
    // display range: below the bottom-most control AND growing the fixed-size panel so a
    // layout pass cannot clip it. (The Portrait editor previously hand-placed the button at
    // ImportButton.Bottom+2 without growing the panel, so it fell out of the display range.)
    [Collection("SharedState")]
    public class FERepoBrowseButtonTests
    {
        [Fact]
        public void AddBrowseButton_GrowsPanel_SoButtonIsNotClipped()
        {
            ExceptionDispatchInfo edi = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // A fixed-size panel just tall enough for one row of buttons — a second row
                    // below would be clipped unless the helper grows the panel.
                    using var panel = new Panel { Size = new Size(240, 40) };
                    using var import = new Button { Size = new Size(107, 20), Location = new Point(11, 11) };
                    using var export = new Button { Size = new Size(107, 20), Location = new Point(125, 11) };
                    panel.Controls.Add(import);
                    panel.Controls.Add(export);

                    Button feRepo = FERepoResourceBrowserForm.AddBrowseButton(import, export, (s, e) => { });

                    Assert.NotNull(feRepo);
                    Assert.True(panel.Controls.Contains(feRepo), "FE-Repo button must be added to the panel");
                    // The whole button must fit within the (grown) panel — no clipping (#1806).
                    Assert.True(feRepo.Bottom <= panel.Height,
                        $"FE-Repo button Bottom ({feRepo.Bottom}) must fit within panel Height ({panel.Height})");
                    // It sits below the existing row, not on top of Import/Export.
                    Assert.True(feRepo.Top >= import.Bottom,
                        $"FE-Repo button Top ({feRepo.Top}) should be below Import.Bottom ({import.Bottom})");
                }
                catch (Exception ex)
                {
                    edi = ExceptionDispatchInfo.Capture(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("STA thread did not complete within 30 seconds");

            edi?.Throw();
        }
    }
}
