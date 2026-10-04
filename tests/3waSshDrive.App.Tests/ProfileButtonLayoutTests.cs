using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class ProfileButtonLayoutTests
    {
        [DataTestMethod]
        [DataRow(1f)]
        [DataRow(1.25f)]
        [DataRow(1.5f)]
        [DataRow(2f)]
        public void MinimumWindow_ProfileButtonsFitTheirRow(float scale)
        {
            MountConcurrencyTests.RunSta(() =>
            {
                using (var form = MountConcurrencyTests.CreateForm(new MountConcurrencyTests.TestRemote(() => { })))
                {
                    form.Size = form.MinimumSize;
                    // Deterministic layout scaling; physical monitor DPI transitions are a manual check.
                    if (scale != 1f)
                    {
                        foreach (var name in new[] { "_newButton", "_saveButton", "_deleteButton", "_cancelOperationButton" })
                        {
                            var button = MountConcurrencyTests.Field<Button>(form, name);
                            button.Font = new Font(button.Font.FontFamily, button.Font.Size * scale, button.Font.Style, button.Font.Unit);
                        }
                        form.Scale(new SizeF(scale, scale));
                    }
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-10000, -10000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    form.PerformLayout();
                    var names = new[] { "_newButton", "_saveButton", "_deleteButton", "_cancelOperationButton" };
                    var first = MountConcurrencyTests.Field<Button>(form, names[0]);
                    var panel = (FlowLayoutPanel)first.Parent;
                    var table = (TableLayoutPanel)panel.Parent;
                    table.PerformLayout();
                    panel.PerformLayout();
                    foreach (var name in names)
                    {
                        var button = MountConcurrencyTests.Field<Button>(form, name);
                        Assert.IsTrue(panel.ClientRectangle.Contains(button.Bounds), name + " clipped by profile toolbar at " + scale);
                        var top = table.GetRowHeights()[0];
                        Assert.IsTrue(panel.Bottom + panel.Margin.Bottom <= top, "Profile toolbar clipped by row at " + scale);
                    }
                    var output = Environment.GetEnvironmentVariable("SSHDRIVE_LAYOUT_EVIDENCE");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        using (var bitmap = new Bitmap(panel.Width, panel.Height))
                        {
                            panel.DrawToBitmap(bitmap, panel.ClientRectangle);
                            bitmap.Save(Path.Combine(output, "profile-buttons-" + (int)(scale * 100) + ".png"));
                        }
                    }
                }
            });
        }
    }
}
