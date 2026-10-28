using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using System.Drawing;

namespace SourceRefresher
{
    /// <summary>
    /// Окно настроек: список правил. Правило состоит из трёх блоков: что обновлять (сцена и источник),
    /// когда обновлять (при запуске OBS или каждые N) и условие (только в тишине), под условием фраза о том, что сделает правило.
    /// Внешний вид повторяет тему OBS (см. ObsTheme).
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        // Размеры карточки для 96 dpi: три блока в ряд, если места хватает, иначе блоки переносятся ниже
        private const int BlockWidth = 392;
        private const int BlockGap = 24;
        private const int CardPad = 16;
        private const int MaxBlockGap = 60;
        private const int SaveDelayMs = 500;

        // Эти элементы пересоздаются при смене темы (см. BuildUi)
        private FlowLayoutPanel _list = null!;
        private Label _emptyLabel = null!;
        private ThemedButton _addBottom = null!;
        private Label _statusLabel = null!;
        private System.Windows.Forms.Timer _statusTimer = null!;
        private ToolTip _tip = null!;
        private readonly List<RuleCard> _cards = new List<RuleCard>();
        private readonly List<string> _scenes = new List<string>();
        private readonly System.Windows.Forms.Timer _liveTimer;
        // Автосохранение: правки копятся, и после короткой паузы без изменений правила записываются и применяются
        private readonly System.Windows.Forms.Timer _saveTimer;
        private string _savedJson = "";
        private long _lastLiveErrorMs;

        public SettingsForm(IReadOnlyList<RefreshRuleConfig> rules)
        {
            Ui.K = DeviceDpi / 96f;

            AutoScaleMode = AutoScaleMode.None;
            Text = Loc.WindowTitle;
            ShowIcon = false;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // ширина окна под три блока, но не шире экрана; на узком экране блоки перенесутся ниже
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
            int scrollbar = SystemInformation.VerticalScrollBarWidth;
            int wide = Ui.Px(CardPad * 2 + BlockWidth * 3 + BlockGap * 2 + 32) + scrollbar;
            ClientSize = new Size(Math.Min(wide, area.Width - Ui.Px(40)), Math.Min(Ui.Px(640), area.Height - Ui.Px(100)));
            MinimumSize = SizeFromClientSize(new Size(Ui.Px(CardPad * 2 + BlockWidth + 32) + scrollbar, Ui.Px(420)));

            LoadScenes();

            // плашки состояния: раз в полсекунды хватает, это не индикатор уровня
            _liveTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _liveTimer.Tick += (s, e) =>
            {
                // Сбой в одной карточке не должен останавливать остальные и повторяться окном ошибки каждые полсекунды:
                // пишем в журнал не чаще раза в 10 секунд
                foreach (var card in _cards.ToArray())
                {
                    try
                    {
                        card.UpdateLive();
                    }
                    catch (Exception ex)
                    {
                        long now = Environment.TickCount64;
                        if (now - _lastLiveErrorMs > 10000)
                        {
                            _lastLiveErrorMs = now;
                            Plugin.LogError($"Settings window: live update failed: {ex}");
                        }
                    }
                }
            };

            _saveTimer = new System.Windows.Forms.Timer { Interval = SaveDelayMs };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                SaveNow();
            };

            ObsTheme.Apply(Plugin.ThemeDark);
            BuildUi(rules);
            _savedJson = SerializeRules(rules);   // что уже лежит в конфиге: по нему отличаем настоящие правки
            _liveTimer.Start();
        }

        // Строит весь интерфейс по текущей теме. Вызывается при открытии окна и при смене темы
        private void BuildUi(IReadOnlyList<RefreshRuleConfig> rules)
        {
            BackColor = ObsTheme.WindowBg;
            ForeColor = ObsTheme.Text;
            Font = Ui.Font();

            _tip = ThemedToolTip.Create();

            _list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = ObsTheme.WindowBg,
                Padding = new Padding(Ui.Px(16), Ui.Px(4), Ui.Px(16), Ui.Px(8))
            };
            var list = _list;
            list.HandleCreated += (s, e) => Ui.ApplyScrollbarTheme(list.Handle);
            list.SizeChanged += (s, e) => ResizeCards();

            _emptyLabel = new Label
            {
                Text = Loc.NoRules,
                AutoSize = false,
                Width = Ui.Px(400),
                Height = Ui.Px(44),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ObsTheme.TextMuted,
                BackColor = ObsTheme.WindowBg
            };
            _list.Controls.Add(_emptyLabel);

            var topBar = new Panel { Dock = DockStyle.Top, Height = Ui.Px(58), BackColor = ObsTheme.WindowBg };
            var btnAdd = new ThemedButton
            {
                Text = Loc.AddRule,
                Glyph = Ui.GlyphAdd,
                Tone = ButtonTone.Success,
                Bounds = new Rectangle(Ui.Px(16), Ui.Px(14), Ui.Px(210), Ui.Px(32))
            };
            btnAdd.Click += (s, e) => AddNewRule();
            topBar.Controls.Add(btnAdd);

            // такая же кнопка в конце списка: после прокрутки к последнему правилу добавлять следующее удобнее оттуда
            _addBottom = new ThemedButton
            {
                Text = Loc.AddRule,
                Glyph = Ui.GlyphAdd,
                Tone = ButtonTone.Success,
                Size = new Size(Ui.Px(210), Ui.Px(32)),
                Margin = new Padding(0, Ui.Px(4), 0, Ui.Px(8))
            };
            _addBottom.Click += (s, e) => AddNewRule();
            _list.Controls.Add(_addBottom);

            // выбор темы справа: подпись и список из двух вариантов
            var themeBox = new Panel { Dock = DockStyle.Right, Width = Ui.Px(250), BackColor = ObsTheme.WindowBg };
            themeBox.Controls.Add(new Label
            {
                Text = Loc.Theme,
                Bounds = new Rectangle(0, Ui.Px(14), Ui.Px(70), Ui.Px(32)),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = ObsTheme.TextMuted,
                BackColor = ObsTheme.WindowBg
            });
            var themePicker = new ThemedPicker(editable: false) { Bounds = new Rectangle(Ui.Px(78), Ui.Px(14), Ui.Px(156), Ui.Px(32)) };
            themePicker.SetItems(new object[] { Loc.ThemeDarkName, Loc.ThemeLightName });
            themePicker.SelectedIndex = ObsTheme.IsDark ? 0 : 1;
            themePicker.SelectionChanged += (s, e) =>
            {
                bool dark = themePicker.SelectedIndex == 0;
                // перестраиваем окно уже после того, как закончится обработка клика по списку
                BeginInvoke(new Action(() => SwitchTheme(dark)));
            };
            themeBox.Controls.Add(themePicker);
            topBar.Controls.Add(themeBox);

            var bottomBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Ui.Px(40),
                BackColor = ObsTheme.BaseBg,
                Padding = new Padding(Ui.Px(16), 0, Ui.Px(16), 0)
            };
            // кнопки «Сохранить» нет: всё сохраняется само, а внизу показывается подсказка и короткое «Сохранено»
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = Loc.AutoSaveHint,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ObsTheme.TextMuted,
                BackColor = ObsTheme.BaseBg
            };
            bottomBar.Controls.Add(_statusLabel);

            var statusLabel = _statusLabel;
            _statusTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            var statusTimer = _statusTimer;
            statusTimer.Tick += (s, e) =>
            {
                statusTimer.Stop();
                statusLabel.ForeColor = ObsTheme.TextMuted;
                statusLabel.Text = Loc.AutoSaveHint;
            };

            // Fill добавляется первым: при docking контролы раскладываются от последнего добавленного к первому
            Controls.Add(_list);
            Controls.Add(topBar);
            Controls.Add(bottomBar);

            foreach (var rule in rules) AddCard(rule);
            UpdateEmptyState();
            ResizeCards();
        }

        // Меняет тему: запоминает её в конфиге и пересобирает окно, сохраняя введённые, но ещё не сохранённые значения
        private void SwitchTheme(bool dark)
        {
            if (dark == ObsTheme.IsDark) return;

            FlushSave();   // Plugin.SetTheme пишет в конфиг то, что уже применено: отложенные правки должны попасть туда раньше

            var current = new List<RefreshRuleConfig>(_cards.Count);
            foreach (var card in _cards) current.Add(card.ToConfig());

            Plugin.SetTheme(dark);
            ObsTheme.Apply(dark);

            SuspendLayout();
            var old = new List<Control>();
            foreach (Control control in Controls) old.Add(control);
            Controls.Clear();
            foreach (var control in old) control.Dispose();
            _cards.Clear();
            _statusTimer.Dispose();
            _tip.Dispose();

            BuildUi(current);
            ResumeLayout(true);

            Ui.ApplyWindowChrome(Handle);
            Invalidate(true);
        }

        // Карточки занимают всю ширину списка, а число колонок внутри карточки зависит от этой ширины.
        // Место под полосу прокрутки резервируется всегда, чтобы раскладка не прыгала при её появлении
        private void ResizeCards()
        {
            if (_list == null) return;

            int width = CardWidth();

            _emptyLabel.Width = width;
            foreach (var card in _cards)
            {
                if (card.Width != width) card.Width = width;
            }
        }

        private int CardWidth()
        {
            // список растянут на всё окно (Dock.Fill), поэтому берём ширину окна: у только что созданного списка своя ширина ещё не посчитана
            return Math.Max(Ui.Px(CardPad * 2 + BlockWidth),
                ClientSize.Width - _list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ApplyWindowChrome(Handle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            FlushSave();   // правки, сделанные меньше чем за паузу до закрытия, не должны потеряться
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _liveTimer.Dispose();
                _saveTimer.Dispose();
                _statusTimer.Dispose();
                _tip.Dispose();
                foreach (var card in _cards) card.Dispose();
                _cards.Clear();
            }
            base.Dispose(disposing);
        }

        private void LoadScenes()
        {
            _scenes.Clear();

            try
            {
                _scenes.AddRange(ObsSceneApi.GetSceneNames());
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to read OBS scenes: {ex.Message}");
            }
        }

        // Порядок Tab: карточки сверху вниз, кнопка «Добавить правило» внизу списка последняя.
        // Control.Add выдаёт каждому новому контролу следующий номер, поэтому после вставки номера выставляются заново;
        // иначе при открытии окна фокус (и прокрутка вместе с ним) уходил бы на кнопку в конце списка
        private void FixTabOrder()
        {
            for (int i = 0; i < _cards.Count; i++) _cards[i].TabIndex = i + 1;
            _addBottom.TabIndex = _cards.Count + 1;
        }

        private void AddNewRule()
        {
            var card = AddCard(new RefreshRuleConfig { id = RefreshRuleConfig.NewId() });
            _list.ScrollControlIntoView(card);
            QueueSave();
        }

        private RuleCard AddCard(RefreshRuleConfig config, RuleCard? after = null)
        {
            // у нового правила название по умолчанию: «Правило N» с первым свободным номером
            if (string.IsNullOrWhiteSpace(config.name)) config.name = NextDefaultName();

            var card = new RuleCard(this, config);
            card.RemoveRequested += (s, e) =>
            {
                // удаление сразу попадает в конфиг, поэтому спрашиваем подтверждение
                string name = card.RuleName.Length > 0 ? card.RuleName : config.name;
                if (!ThemedDialog.Confirm(this, Loc.ConfirmRemoveTitle, Loc.ConfirmRemoveBody(name), ButtonTone.Danger)) return;

                _cards.Remove(card);
                _list.Controls.Remove(card);
                card.Dispose();
                UpdateEmptyState();
                ResizeCards();
                SaveNow();
            };
            card.DuplicateRequested += (s, e) =>
            {
                var copy = card.ToConfig();
                copy.id = RefreshRuleConfig.NewId();
                copy.name = Loc.CopyName(card.RuleName);
                var clone = AddCard(copy, after: card);
                _list.ScrollControlIntoView(clone);
                QueueSave();
            };
            card.Changed += (s, e) => QueueSave();

            // ширина задаётся до добавления в список: иначе карточка с шириной по умолчанию на миг становится одноколоночной
            // (очень высокой), и область прокрутки запоминает эту высоту, оставляя пустое место под последним правилом
            card.Width = CardWidth();

            // копия встаёт сразу после оригинала, новое правило в конец (перед кнопкой «Добавить правило» внизу списка)
            int index = after != null ? _cards.IndexOf(after) + 1 : _cards.Count;
            _cards.Insert(index, card);
            _list.Controls.Add(card);
            _list.Controls.SetChildIndex(card, _list.Controls.IndexOf(_emptyLabel) + 1 + index);
            FixTabOrder();
            ResizeCards();
            UpdateEmptyState();
            return card;
        }

        private string NextDefaultName()
        {
            for (int number = 1; ; number++)
            {
                string candidate = Loc.RuleTitle(number);
                if (!_cards.Exists(c => string.Equals(c.RuleName, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
            }
        }

        private void UpdateEmptyState()
        {
            _emptyLabel.Visible = _cards.Count == 0;
        }

        // Любая правка запускает отсчёт заново: запись идёт после паузы, а не на каждое нажатие клавиши
        private void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // Записывает отложенные правки сразу (перед сменой темы и при закрытии окна)
        private void FlushSave()
        {
            if (!_saveTimer.Enabled) return;
            _saveTimer.Stop();
            SaveNow();
        }

        private List<RefreshRuleConfig> CollectRules()
        {
            var rules = new List<RefreshRuleConfig>(_cards.Count);
            foreach (var card in _cards) rules.Add(card.ToConfig());
            return rules;
        }

        private static string SerializeRules(IReadOnlyList<RefreshRuleConfig> rules)
        {
            // Clone приводит значения к допустимым: бесконечность и NaN (их можно получить только из кода) не записываются в JSON
            var config = new PluginConfig { rules = new List<RefreshRuleConfig>(rules.Count) };
            foreach (var rule in rules) config.rules.Add((rule ?? new RefreshRuleConfig()).Clone());
            return JsonSerializer.Serialize(config, PluginConfigJsonContext.Default.PluginConfig);
        }

        // Записывает правила в конфиг и применяет их. Если ничего не изменилось (например, просто открыли список), конфиг не трогаем
        private void SaveNow()
        {
            // исключение из таймера окна убило бы окно настроек, поэтому ошибку только пишем в журнал
            string json;
            bool written;
            try
            {
                var rules = CollectRules();
                json = SerializeRules(rules);
                if (string.Equals(json, _savedJson, StringComparison.Ordinal)) return;

                written = Plugin.SaveConfig(rules);
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Failed to apply rules: {ex.Message}");
                return;
            }

            if (!written)
            {
                // правила применены, но на диск не записались (нет прав, диск переполнен): говорим об этом и пробуем снова при следующей правке
                _statusLabel.Text = Loc.SaveFailed;
                _statusLabel.ForeColor = ObsTheme.DangerAccent;
                _statusTimer.Stop();
                return;
            }
            _savedJson = json;

            // окно остаётся открытым, о сохранении сообщаем коротким текстом внизу
            _statusLabel.Text = Loc.Saved;
            _statusLabel.ForeColor = ObsTheme.Text;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        // Источник в списке выбора: название, признак «можно обновить» (по нему рисуется значок справа)
        // и признак «сейчас в эфире» (неактивный источник показывается серым)
        private sealed class SourceItem : IBadgedItem
        {
            public SourceItem(string name, bool canRefresh, bool active) { Name = name; CanRefresh = canRefresh; Active = active; }

            public string Name { get; }
            public bool CanRefresh { get; }
            public bool Active { get; }
            public bool HasBadge => CanRefresh;
            public bool Dimmed => !Active;

            public override string ToString() => Name;
        }

        // Карточка одного правила
        private sealed class RuleCard : CardPanel
        {
            private const int RowPitch = 40;
            private const int LabelWidth = 96;
            private const int SummaryHeight = 96;
            private const int SupportRecheckTicks = 10;   // проверка «можно ли обновить источник» раз в 5 секунд

            private readonly SettingsForm _owner;
            private readonly string _id;

            // скрытые из окна настройки тишины: хранятся, чтобы правка конфига вручную не терялась при сохранении из окна
            private readonly int _silenceSeconds;
            private readonly float _silenceDb;

            // заголовок: переключатель, название, плашка состояния, копирование и удаление
            private readonly ThemedSwitch _enabled;
            private readonly ThemedTextBox _name;
            private readonly StatusChip _chip;
            private readonly ThemedButton _nameFromSource;
            private readonly ThemedButton _duplicate;
            private readonly ThemedButton _remove;

            // блок «Что обновлять»
            private readonly Label _whatTitle;
            private readonly Label _sceneLabel;
            private readonly ThemedPicker _scene;
            private readonly Label _sourceLabel;
            private readonly ThemedPicker _source;
            private readonly WarningIcon _warnIcon;
            private readonly Label _warnText;

            // блок «Когда обновлять»
            private readonly Label _whenTitle;
            private readonly Label _modeLabel;
            private readonly ThemedPicker _mode;
            private readonly Label _everyLabel;
            private readonly NumberStepper _hours;
            private readonly NumberStepper _minutes;
            private readonly NumberStepper _seconds;

            // блок «Условие» и фраза-сводка
            private readonly Label _conditionTitle;
            private readonly ThemedCheckBox _onlySilent;
            private readonly ThemedButton _silentHelp;
            private readonly Label _summaryTitle;
            private readonly SummaryText _summary;

            private RefreshSupport _support = RefreshSupport.NotFound;
            private int _supportTicks;
            private bool _normalizing;

            public event EventHandler? RemoveRequested;
            public event EventHandler? DuplicateRequested;

            /// <summary>Пользователь изменил любое значение правила: окно сохранит его после короткой паузы</summary>
            public event EventHandler? Changed;

            public string RuleName => _name.Text.Trim();

            private bool IsInterval => _mode.SelectedIndex == 1;

            public RuleCard(SettingsForm owner, RefreshRuleConfig config)
            {
                _owner = owner;
                var clean = config.Clone();
                _id = clean.id;
                _silenceSeconds = clean.silence_seconds;
                _silenceDb = clean.silence_db;
                Margin = new Padding(0, 0, 0, Ui.Px(12));

                int inputH = Ui.Px(30);

                // заголовок: переключатель, название (плоское поле с карандашом), плашка состояния, копирование и удаление
                _enabled = new ThemedSwitch { AccessibleName = Loc.SwitchTip };
                _name = new ThemedTextBox { Font = Ui.Font(FontStyle.Bold, 1.1f), Height = inputH, Flat = true, AccessibleName = Loc.EditNameTip };
                _chip = new StatusChip();
                // кнопка только со значком копирования: подставляет название выбранного источника в название правила
                _nameFromSource = new ThemedButton { Glyph = Ui.GlyphCopy, AccessibleName = Loc.CopySourceNameTip };
                _nameFromSource.Click += (s, e) => CopySourceNameToRuleName();
                _duplicate = new ThemedButton { Glyph = Ui.GlyphCopy, AccessibleName = Loc.DuplicateRule };
                _duplicate.Click += (s, e) => DuplicateRequested?.Invoke(this, EventArgs.Empty);
                _remove = new ThemedButton { Glyph = Ui.GlyphDelete, Tone = ButtonTone.Danger, AccessibleName = Loc.RemoveRule };
                _remove.Click += (s, e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
                owner._tip.SetToolTip(_enabled, Loc.SwitchTip);
                owner._tip.SetToolTip(_name, Loc.EditNameTip);
                owner._tip.SetToolTip(_nameFromSource, Loc.CopySourceNameTip);
                owner._tip.SetToolTip(_duplicate, Loc.DuplicateRule);
                owner._tip.SetToolTip(_remove, Loc.RemoveRule);

                _whatTitle = MakeLabel(Loc.WhatTitle, bold: true);
                _sceneLabel = MakeLabel(Loc.Scene);
                _scene = new ThemedPicker(editable: false) { Height = inputH, AccessibleName = Loc.Scene };
                _sourceLabel = MakeLabel(Loc.Source);
                _source = new ThemedPicker(editable: false) { Height = inputH, AccessibleName = Loc.Source, BadgeLegend = Loc.RefreshBadgeLegend, DimLegend = Loc.InactiveLegend };
                _warnIcon = new WarningIcon { Visible = false };
                _warnText = MakeLabel(Loc.NoRefreshWarning, color: ObsTheme.WarnAccent);
                _warnText.Visible = false;
                _warnText.TextAlign = ContentAlignment.TopLeft;
                owner._tip.SetToolTip(_warnIcon, Loc.NoRefreshWarning);

                _whenTitle = MakeLabel(Loc.WhenTitle, bold: true);
                _modeLabel = MakeLabel(Loc.Mode);
                _mode = new ThemedPicker(editable: false) { Height = inputH, AccessibleName = Loc.Mode };
                _everyLabel = MakeLabel(Loc.Every);
                _hours = new NumberStepper { Height = inputH, Maximum = 99, Suffix = Loc.UnitHours, AccessibleName = Loc.UnitHours };
                _minutes = new NumberStepper { Height = inputH, Maximum = 59, Suffix = Loc.UnitMinutes, AccessibleName = Loc.UnitMinutes };
                _seconds = new NumberStepper { Height = inputH, Maximum = 59, Suffix = Loc.UnitSeconds, AccessibleName = Loc.UnitSeconds };

                _conditionTitle = MakeLabel(Loc.ConditionTitle, bold: true);
                _onlySilent = new ThemedCheckBox { Text = Loc.OnlySilent, AccessibleName = Loc.OnlySilent };
                _silentHelp = MakeHelpButton(Loc.OnlySilentHelp(_silenceSeconds));
                _summaryTitle = MakeLabel(Loc.SummaryTitle, bold: true);
                _summary = new SummaryText();

                // порядок добавления задаёт порядок Tab
                foreach (Control control in new Control[]
                {
                    _enabled, _name, _nameFromSource, _chip, _duplicate, _remove,
                    _whatTitle, _sceneLabel, _scene, _sourceLabel, _source, _warnIcon, _warnText,
                    _whenTitle, _modeLabel, _mode, _everyLabel, _hours, _minutes, _seconds,
                    _conditionTitle, _onlySilent, _silentHelp, _summaryTitle, _summary
                })
                {
                    Controls.Add(control);
                }

                Fill(clean);
                WireEvents();
                WireChanged();
                RefreshSupportState();
                RefreshInfo();
                Relayout();
            }

            private Label MakeLabel(string text, bool bold = false, Color? color = null)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Font = Ui.Font(bold ? FontStyle.Bold : FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = color ?? ObsTheme.Text,
                    BackColor = ObsTheme.BaseBg
                };
            }

            // Круглая кнопка «?» с подсказкой
            private ThemedButton MakeHelpButton(string tip)
            {
                var button = new ThemedButton
                {
                    Text = "?",
                    Round = true,
                    TabStop = false,
                    Font = Ui.Font(FontStyle.Bold, 0.9f),
                    Size = new Size(Ui.Px(24), Ui.Px(24)),
                    AccessibleName = tip
                };
                _owner._tip.SetToolTip(button, tip);
                return button;
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (_name != null) Relayout();
            }

            // --- раскладка: три блока в ряд, а на узком окне блоки переносятся ниже ---

            private void Relayout()
            {
                int pad = Ui.Px(CardPad), blockW = Ui.Px(BlockWidth), gap = Ui.Px(BlockGap);
                int rowH = Ui.Px(RowPitch), headerH = Ui.Px(54);
                bool interval = IsInterval;

                // высоты блоков: у «Что обновлять» ещё строка предупреждения, у «Условия» условие и фраза-сводка
                int hA = (_warnIcon.Visible ? 4 : 3) * rowH;
                int hB = 3 * rowH;
                int hC = interval ? 2 * rowH + Ui.Px(4) + Ui.Px(30) + Ui.Px(SummaryHeight) : Ui.Px(30) + Ui.Px(SummaryHeight);

                int cols = Width >= pad * 2 + blockW * 3 + gap * 2 ? 3 : Width >= pad * 2 + blockW * 2 + gap ? 2 : 1;

                // свободная ширина распределяется между колонками, но не бесконечно
                int extra = Math.Max(0, Width - (pad * 2 + blockW * cols + gap * (cols - 1)));
                int spread = cols > 1 ? Math.Min(Ui.Px(MaxBlockGap - BlockGap), extra / (cols - 1)) : 0;
                int step = blockW + gap + spread;

                Point a = new Point(pad, headerH), b, c;
                int height;
                if (cols == 3)
                {
                    b = new Point(pad + step, headerH);
                    c = new Point(pad + step * 2, headerH);
                    height = headerH + Math.Max(hA, Math.Max(hB, hC)) + pad;
                }
                else if (cols == 2)
                {
                    b = new Point(pad + step, headerH);
                    c = new Point(pad, headerH + Math.Max(hA, hB) + Ui.Px(12));
                    height = c.Y + hC + pad;
                }
                else
                {
                    b = new Point(pad, headerH + hA + Ui.Px(12));
                    c = new Point(pad, b.Y + hB + Ui.Px(12));
                    height = c.Y + hC + pad;
                }

                LayoutHeader();
                PlaceWhatBlock(a, blockW);
                PlaceWhenBlock(b, blockW);
                PlaceConditionBlock(c, blockW);

                if (Height != height) Height = height;
            }

            // Заголовок: переключатель, название (по ширине текста), плашка состояния; справа копирование и удаление
            private void LayoutHeader()
            {
                int pad = Ui.Px(CardPad);
                int switchW = Ui.Px(40), iconW = Ui.Px(36), gap = Ui.Px(10);

                _duplicate.SetBounds(Width - pad - iconW * 2 - Ui.Px(8), Ui.Px(9), iconW, Ui.Px(30));
                _remove.SetBounds(Width - pad - iconW, Ui.Px(9), iconW, Ui.Px(30));
                _enabled.SetBounds(pad, Ui.Px(13), switchW, Ui.Px(22));

                int chipW = _chip.PreferredWidth;
                int nameX = pad + switchW + gap;
                int copyW = Ui.Px(32), copyGap = Ui.Px(6);
                int room = _duplicate.Left - gap - nameX - copyGap - copyW - gap - chipW;
                int wanted = TextRenderer.MeasureText(_name.Text, _name.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + Ui.Px(52);
                int nameW = Math.Max(Ui.Px(100), Math.Min(Math.Max(wanted, Ui.Px(110)), room));

                _name.SetBounds(nameX, Ui.Px(9), nameW, Ui.Px(30));
                _nameFromSource.SetBounds(nameX + nameW + copyGap, Ui.Px(9), copyW, Ui.Px(30));
                _chip.SetBounds(nameX + nameW + copyGap + copyW + gap, Ui.Px(12), chipW, Ui.Px(24));
            }

            private void PlaceRow(Label label, Control control, Point origin, int row, int blockW, int controlWidth = -1)
            {
                int y = origin.Y + row * Ui.Px(RowPitch);
                int controlX = origin.X + Ui.Px(LabelWidth + 6);
                label.SetBounds(origin.X, y, Ui.Px(LabelWidth), Ui.Px(30));
                control.SetBounds(controlX, y, controlWidth > 0 ? controlWidth : blockW - Ui.Px(LabelWidth + 6), control.Height);
            }

            private void PlaceWhatBlock(Point o, int blockW)
            {
                int iconW = Ui.Px(26);
                bool warn = _warnIcon.Visible;

                _whatTitle.SetBounds(o.X, o.Y, blockW, Ui.Px(30));
                PlaceRow(_sceneLabel, _scene, o, 1, blockW);

                // значок предупреждения стоит справа от списка источников и забирает у него место, только когда он виден
                int sourceW = blockW - Ui.Px(LabelWidth + 6) - (warn ? iconW + Ui.Px(8) : 0);
                PlaceRow(_sourceLabel, _source, o, 2, blockW, sourceW);
                _warnIcon.SetBounds(o.X + blockW - iconW, o.Y + 2 * Ui.Px(RowPitch) + Ui.Px(2), iconW, iconW);
                _warnText.SetBounds(o.X, o.Y + 3 * Ui.Px(RowPitch) - Ui.Px(2), blockW, Ui.Px(44));
            }

            private void PlaceWhenBlock(Point o, int blockW)
            {
                bool interval = IsInterval;

                _whenTitle.SetBounds(o.X, o.Y, blockW, Ui.Px(30));
                PlaceRow(_modeLabel, _mode, o, 1, blockW);

                // три поля времени делят оставшуюся ширину строки
                int fieldsX = o.X + Ui.Px(LabelWidth + 6);
                int fieldsW = blockW - Ui.Px(LabelWidth + 6);
                int gap = Ui.Px(8);
                int each = (fieldsW - gap * 2) / 3;
                int y = o.Y + 2 * Ui.Px(RowPitch);

                _everyLabel.SetBounds(o.X, y, Ui.Px(LabelWidth), Ui.Px(30));
                _hours.SetBounds(fieldsX, y, each, _hours.Height);
                _minutes.SetBounds(fieldsX + each + gap, y, each, _minutes.Height);
                _seconds.SetBounds(fieldsX + (each + gap) * 2, y, each, _seconds.Height);

                _everyLabel.Visible = _hours.Visible = _minutes.Visible = _seconds.Visible = interval;
            }

            private void PlaceConditionBlock(Point o, int blockW)
            {
                bool interval = IsInterval;
                int summaryY = o.Y;

                if (interval)
                {
                    _conditionTitle.SetBounds(o.X, o.Y, blockW, Ui.Px(30));
                    int helpW = Ui.Px(24);
                    int checkW = Math.Min(blockW - helpW - Ui.Px(8),
                        TextRenderer.MeasureText(_onlySilent.Text, _onlySilent.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + Ui.Px(40));
                    _onlySilent.SetBounds(o.X, o.Y + Ui.Px(RowPitch), checkW, Ui.Px(28));
                    _silentHelp.SetBounds(o.X + checkW + Ui.Px(8), o.Y + Ui.Px(RowPitch) + Ui.Px(2), helpW, helpW);
                    summaryY = o.Y + 2 * Ui.Px(RowPitch) + Ui.Px(4);
                }

                _conditionTitle.Visible = _onlySilent.Visible = _silentHelp.Visible = interval;

                _summaryTitle.SetBounds(o.X, summaryY, blockW, Ui.Px(26));
                _summary.SetBounds(o.X, summaryY + Ui.Px(30), blockW, Ui.Px(SummaryHeight));
            }

            // --- значения ---

            private void Fill(RefreshRuleConfig config)
            {
                _name.Text = config.name;
                _enabled.Checked = config.enabled;

                // сцену могли удалить или переименовать, тогда показываем сохранённое значение
                var scenes = new List<string>(_owner._scenes);
                if (config.scene_name.Length > 0 && !scenes.Contains(config.scene_name)) scenes.Add(config.scene_name);
                _scene.SetItems(scenes);

                // новое правило: сцена не выбрана, источник недоступен, пока не выбрана сцена
                bool hasScene = config.scene_name.Length > 0;
                if (hasScene)
                {
                    _scene.SelectedItem = config.scene_name;
                    RefreshSources(config.source_name);
                }
                _source.Enabled = hasScene;

                _mode.SetItems(new object[] { Loc.ModeStartup, Loc.ModeInterval });
                _mode.SelectedIndex = config.IsStartup ? 0 : 1;

                SetTime(config.interval_seconds);
                _onlySilent.Checked = config.only_silent;
            }

            private void SetTime(int totalSeconds)
            {
                _hours.Value = totalSeconds / 3600;
                _minutes.Value = totalSeconds % 3600 / 60;
                _seconds.Value = totalSeconds % 60;
            }

            private int RawTotalSeconds() => _hours.Value * 3600 + _minutes.Value * 60 + _seconds.Value;

            private int TotalSeconds() => RefreshRuleConfig.ClampInterval(RawTotalSeconds());

            // Время короче минимального поднимается до него и показывается уже исправленным (пока в поле печатают, его не трогаем)
            private void NormalizeTime()
            {
                if (_normalizing) return;
                _normalizing = true;
                try
                {
                    int total = RawTotalSeconds();
                    if (total < RefreshRuleConfig.MinIntervalSeconds) _seconds.Value = RefreshRuleConfig.MinIntervalSeconds - _hours.Value * 3600 - _minutes.Value * 60;
                }
                finally
                {
                    _normalizing = false;
                }
            }

            private void WireEvents()
            {
                // при смене сцены выбранный источник сбрасывается
                _scene.SelectionChanged += (s, e) =>
                {
                    _source.Enabled = _scene.SelectedItem != null;
                    RefreshSources(null);
                    RefreshSupportState();
                };
                _source.SelectionChanged += (s, e) => RefreshSupportState();

                // список источников обновляется при открытии, вдруг в сцену добавили новый
                _source.DropDownOpening += (s, e) => RefreshSources(_source.Text);

                // режим меняет набор полей и высоту карточки
                _mode.SelectionChanged += (s, e) =>
                {
                    Relayout();
                    RefreshInfo();
                };

                foreach (var stepper in new[] { _hours, _minutes, _seconds }) stepper.Committed += (s, e) => NormalizeTime();
            }

            // Все поля, которые пользователь может менять. Подключается после WireEvents, чтобы к моменту сигнала
            // внутренние поля карточки уже были обновлены, и после Fill, чтобы заполнение не считалось правкой
            private void WireChanged()
            {
                void Raise(object? s, EventArgs e)
                {
                    RefreshInfo();
                    Changed?.Invoke(this, EventArgs.Empty);
                }

                // Длина ввода ограничена: бесконечное название ломало бы заголовок и окно подтверждения
                _name.InnerTextBox.MaxLength = 80;

                _name.TextChanged += (s, e) => LayoutHeader();   // поле названия растёт и сжимается вместе с текстом
                _name.TextChanged += Raise;
                _enabled.CheckedChanged += Raise;
                _scene.SelectionChanged += Raise;
                _source.SelectionChanged += Raise;
                _mode.SelectionChanged += Raise;
                _hours.ValueChanged += Raise;
                _minutes.ValueChanged += Raise;
                _seconds.ValueChanged += Raise;
                _onlySilent.CheckedChanged += Raise;
            }

            private static List<string> ReadSources(string sceneName)
            {
                try
                {
                    return ObsSceneApi.GetSourceNames(sceneName);
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"Failed to read sources of scene '{sceneName}': {ex.Message}");
                    return new List<string>();
                }
            }

            private void RefreshSources(string? keep)
            {
                var names = new List<string>();

                if (_scene.SelectedItem is string scene)
                {
                    names.AddRange(ReadSources(scene));

                    // сохранённого источника может не быть в списке (удалён, переименован)
                    if (!string.IsNullOrEmpty(keep) && !names.Contains(keep)) names.Add(keep);
                }

                // у каждого источника проверяем, можно ли его обновить: такие помечаются значком в списке
                var items = new List<SourceItem>(names.Count);
                foreach (var name in names) items.Add(new SourceItem(name, ObsRefresher.GetSupport(name) == RefreshSupport.Supported, ObsRefresher.IsActive(name)));

                _source.SetItems(items);
                if (!string.IsNullOrEmpty(keep))
                {
                    int index = items.FindIndex(item => item.Name == keep);
                    if (index >= 0) _source.SelectedIndex = index;
                }
            }

            // --- состояние: можно ли обновить источник, плашка, фраза-сводка ---

            private string SelectedSource => (_source.SelectedItem as SourceItem)?.Name ?? "";

            // Проверяет, есть ли у выбранного источника чем обновляться, и показывает или прячет предупреждение
            private void RefreshSupportState()
            {
                string source = SelectedSource;
                _support = source.Length == 0 ? RefreshSupport.NotFound : ObsRefresher.GetSupport(source);
                _supportTicks = 0;

                bool warn = _support == RefreshSupport.Unsupported;
                if (_warnIcon.Visible != warn)
                {
                    _warnIcon.Visible = _warnText.Visible = warn;
                    Relayout();
                }
            }

            private void RefreshInfo()
            {
                UpdateSummary();
                UpdateChip();

                // копировать нечего, пока источник не выбран, и незачем, когда название уже такое
                _nameFromSource.Enabled = SelectedSource.Length > 0 && !string.Equals(RuleName, SelectedSource, StringComparison.Ordinal);
            }

            // Название правила становится названием выбранного источника (в пределах длины, которую можно ввести вручную)
            private void CopySourceNameToRuleName()
            {
                string source = SelectedSource;
                if (source.Length == 0) return;

                _name.Text = source.Length > _name.InnerTextBox.MaxLength ? source.Substring(0, _name.InnerTextBox.MaxLength) : source;
            }

            // Одна фраза о том, что сделает правило с текущими настройками; обновляется вместе с правками
            private void UpdateSummary()
            {
                string source = SelectedSource;
                if (source.Length == 0)
                {
                    _summary.SetMarkup(Loc.SummaryPick);
                    return;
                }

                if (!IsInterval)
                {
                    _summary.SetMarkup(Loc.SummaryStartup(source));
                    return;
                }

                string time = Loc.Duration(TotalSeconds());
                _summary.SetMarkup(_onlySilent.Checked
                    ? Loc.SummaryIntervalSilent(source, time, Loc.Duration(_silenceSeconds))
                    : Loc.SummaryInterval(source, time));
            }

            private void UpdateChip()
            {
                string text;
                bool active = false;

                if (!_enabled.Checked)
                {
                    text = Loc.ChipOff;
                }
                else if (SelectedSource.Length == 0)
                {
                    text = Loc.ChipNotReady;
                }
                else
                {
                    var status = RefreshManager.GetStatus(_id);
                    switch (status?.Kind)
                    {
                        case RuleStatusKind.SourceNotFound: text = Loc.ChipNotFound; break;
                        case RuleStatusKind.SceneInactive: text = Loc.ChipSceneInactive; break;
                        case RuleStatusKind.SourceInactive: text = Loc.ChipSourceInactive; break;
                        case RuleStatusKind.WaitingStartup: text = Loc.ChipWaitingStartup; active = true; break;
                        case RuleStatusKind.StartupDone: text = Loc.ChipStartupDone; break;
                        case RuleStatusKind.WaitingSilence: text = Loc.ChipWaitingSilence; active = true; break;
                        case RuleStatusKind.Counting:
                            text = status!.Value.RemainingSeconds > 0 ? Loc.ChipCounting(Loc.Countdown(status.Value.RemainingSeconds)) : Loc.ChipRefreshingSoon;
                            break;
                        // правило ещё не применено (его только что создали или изменили): плашка остаётся прежней,
                        // а если прежней нет, пишем, что правило применяется
                        default:
                            if (_chip.Text.Length > 0) return;
                            text = Loc.ChipApplying;
                            break;
                    }
                }

                _chip.Active = active;
                if (_chip.Text != text)
                {
                    _chip.Text = text;
                    LayoutHeader();   // плашка меняет ширину вместе с текстом
                }
            }

            /// <summary>Вызывается таймером окна: обновляет плашку состояния и время от времени проверяет, можно ли обновить источник</summary>
            public void UpdateLive()
            {
                if (++_supportTicks >= SupportRecheckTicks) RefreshSupportState();
                UpdateChip();
            }

            public RefreshRuleConfig ToConfig()
            {
                return new RefreshRuleConfig
                {
                    id = _id,
                    name = RuleName,
                    enabled = _enabled.Checked,
                    scene_name = _scene.SelectedItem as string ?? "",
                    source_name = SelectedSource,
                    mode = IsInterval ? RefreshRuleConfig.ModeInterval : RefreshRuleConfig.ModeStartup,
                    interval_seconds = TotalSeconds(),
                    only_silent = _onlySilent.Checked,
                    silence_seconds = _silenceSeconds,
                    silence_db = _silenceDb
                };
            }
        }
    }
}
