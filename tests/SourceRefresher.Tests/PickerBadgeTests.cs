using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace SourceRefresher.Tests
{
    public class PickerBadgeTests : IClassFixture<PluginFixture>
    {
        public PickerBadgeTests(PluginFixture fixture) { _ = fixture; }

        private sealed class Item : IBadgedItem
        {
            private readonly string _name;
            public Item(string name, bool badge) { _name = name; HasBadge = badge; }
            public bool HasBadge { get; }
            public override string ToString() => _name;
        }

        [Fact]
        public void PopupWithBadgesAndLegend_Opens_AndSelectionStillWorks()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    ObsTheme.Apply(true);
                    using var form = new Form { ClientSize = new Size(400, 300), StartPosition = FormStartPosition.Manual, Location = new Point(50, 50) };
                    var picker = new ThemedPicker(editable: false) { Bounds = new Rectangle(20, 20, 300, 30), BadgeLegend = "Источник можно обновить" };
                    form.Controls.Add(picker);

                    var items = new List<Item>();
                    for (int i = 0; i < 12; i++) items.Add(new Item("Источник " + i, i % 3 == 0));
                    picker.SetItems(items);
                    form.Show();
                    Application.DoEvents();

                    picker.SelectedIndex = 3;   // с значком: рисуется в закрытом поле
                    picker.OpenDropDown(userInitiated: false);
                    Application.DoEvents();
                    Assert.True(picker.IsDropDownOpen);

                    string? shots = Environment.GetEnvironmentVariable("SHOT_DIR");
                    if (shots != null)
                    {
                        using var bmp = new Bitmap(420, 400);
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(form.Location, Point.Empty, bmp.Size);
                        bmp.Save(Path.Combine(shots, "popup.png"));
                    }

                    picker.CloseDropDown();
                    Assert.Equal("Источник 3", picker.Text);
                    Assert.True(picker.SelectedItem is IBadgedItem { HasBadge: true });
                    form.Close();
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(60000));
            if (error != null) throw new Exception("Сбой в потоке окна", error);
        }

        [Fact]
        public void ObsRefresherSupportOverride_IsUsedByCheck()
        {
            ObsRefresher.SupportOverride = name => name == "a" ? RefreshSupport.Supported : RefreshSupport.Unsupported;
            try
            {
                Assert.Equal(RefreshSupport.Supported, ObsRefresher.GetSupport("a"));
                Assert.Equal(RefreshSupport.Unsupported, ObsRefresher.GetSupport("b"));
            }
            finally
            {
                ObsRefresher.SupportOverride = null;
            }
        }
    }
}
