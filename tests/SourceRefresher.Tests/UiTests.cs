using System.Windows.Forms;
using Xunit;

namespace SourceRefresher.Tests
{
    public class UiTests : IClassFixture<PluginFixture>
    {
        private readonly PluginFixture _fx;

        public UiTests(PluginFixture fixture)
        {
            _fx = fixture;
            try { File.Delete(_fx.ConfigPath); } catch { }
        }

        // Окна WinForms живут в своём STA-потоке, как в плагине
        private static void RunSta(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(60000), "окно зависло");
            if (error != null) throw new Exception("Сбой в потоке окна", error);
        }

        [Fact]
        public void NumberStepper_ClampsAndRaisesEvents()
        {
            RunSta(() =>
            {
                ObsTheme.Apply(true);
                using var stepper = new NumberStepper { Maximum = 59, Suffix = "мин" };
                int changes = 0, commits = 0;
                stepper.ValueChanged += (s, e) => changes++;
                stepper.Committed += (s, e) => commits++;

                stepper.Value = 70;
                Assert.Equal(59, stepper.Value);
                stepper.Value = -3;
                Assert.Equal(0, stepper.Value);
                stepper.Value = 0;   // то же значение: события нет
                Assert.Equal(2, changes);
                Assert.Equal(0, commits);

                stepper.Maximum = 10;
                stepper.Value = 25;
                Assert.Equal(10, stepper.Value);
            });
        }

        [Fact]
        public void Window_OpensWithManyRules_AndCloseWithoutEditsDoesNotWriteConfig()
        {
            RunSta(() =>
            {
                var rules = new List<RefreshRuleConfig>();
                for (int i = 0; i < 60; i++)
                {
                    var rule = i % 2 == 0 ? Rules.Interval(300 + i, i % 4 == 0) : Rules.Startup();
                    rule.name = new string('Я', i % 7 == 0 ? 80 : 10) + i;
                    rules.Add(rule);
                }
                rules.Add(new RefreshRuleConfig { id = "", interval_seconds = int.MaxValue, mode = "?" });

                using var form = new SettingsForm(rules);
                form.Show();
                Application.DoEvents();
                foreach (var size in new[] { new System.Drawing.Size(600, 500), new System.Drawing.Size(1400, 800), new System.Drawing.Size(900, 300) })
                {
                    form.Size = size;
                    Application.DoEvents();
                }
                form.Close();
            });

            Assert.False(File.Exists(_fx.ConfigPath));
        }

        [Fact]
        public void Window_WithSourceThatCannotRefresh_Opens()
        {
            ObsRefresher.SupportOverride = name => RefreshSupport.Unsupported;
            try
            {
                RunSta(() =>
                {
                    using var form = new SettingsForm(new[] { Rules.Interval(300, true), Rules.Startup() });
                    form.Show();
                    Application.DoEvents();
                    form.Close();
                });
            }
            finally
            {
                ObsRefresher.SupportOverride = null;
            }
        }

        [Fact]
        public void CopySourceNameButton_PutsSourceNameIntoRuleName()
        {
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

            RunSta(() =>
            {
                var withSource = Rules.Interval(300, true, source: "Музыка наград");
                withSource.name = "Правило 1";
                var withoutSource = Rules.Startup(source: "");
                withoutSource.name = "Правило 2";

                using var form = new SettingsForm(new[] { withSource, withoutSource });
                form.Show();
                Application.DoEvents();

                var cards = (System.Collections.IList)typeof(SettingsForm).GetField("_cards", any)!.GetValue(form)!;
                ThemedButton ButtonOf(object card) => (ThemedButton)card.GetType().GetField("_nameFromSource", any)!.GetValue(card)!;
                RefreshRuleConfig ConfigOf(object card) => (RefreshRuleConfig)card.GetType().GetMethod("ToConfig", any)!.Invoke(card, null)!;

                // без выбранного источника копировать нечего
                Assert.False(ButtonOf(cards[1]!).Enabled);

                var button = ButtonOf(cards[0]!);
                Assert.True(button.Enabled);
                typeof(Control).GetMethod("OnClick", any)!.Invoke(button, new object[] { EventArgs.Empty });
                Application.DoEvents();

                Assert.Equal("Музыка наград", ConfigOf(cards[0]!).name);
                Assert.False(button.Enabled);   // название уже такое
                Assert.Equal("Правило 2", ConfigOf(cards[1]!).name);
                form.Close();
            });
        }

        [Fact]
        public void Window_BothThemes_Open()
        {
            RunSta(() =>
            {
                foreach (bool dark in new[] { true, false })
                {
                    Plugin.SetTheme(dark);
                    using var form = new SettingsForm(new[] { Rules.Interval(300, true) });
                    form.Show();
                    Application.DoEvents();
                    form.Close();
                }
            });
        }
    }
}
