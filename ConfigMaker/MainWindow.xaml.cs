﻿using ConfigMaker.Csgo.Commands;
using ConfigMaker.Csgo.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Data;
using ConfigMaker.Csgo.Config.interfaces;
using ConfigMaker.Csgo.Config.Entries;
using ConfigMaker.Converters;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;
using System.Windows.Documents;
using Res = ConfigMaker.Properties.Resources;
using System.Resources;

namespace ConfigMaker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Properties/fields/DependencyProperties
        public enum EntryStateBinding
        {
            KeyDown,
            KeyUp,
            Default,
            Alias,
            InvalidState
        }

        ConfigManager cfgManager = new ConfigManager();
        KeySequence currentKeySequence = null;

        Dictionary<ConfigEntry, EntryUiBinding> entryUiBindings = new Dictionary<ConfigEntry, EntryUiBinding>();

        public EntryStateBinding StateBinding
        {
            get => (EntryStateBinding) GetValue(StateBindingProperty);
            set
            {
                SetValue(StateBindingProperty, value);
                // Т.к. привязка задается только в коде, то тут же обновим интерфейс. TODO: Remove
                this.entryUiBindings.Values.ToList()
                    .ForEach(entry => entry.HandleState(this.StateBinding));
            }   
        }

        public static readonly DependencyProperty StateBindingProperty;

        static MainWindow()
        {
            StateBindingProperty = DependencyProperty.Register(
                "StateBinding",
                typeof(EntryStateBinding),
                typeof(MainWindow),
                new PropertyMetadata(EntryStateBinding.Default));
        }
        #endregion

        #region UI
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Добавляем слушателя на нажатие виртуальной клавиатуры
            this.kb.OnKeyboardKeyDown += KeyboardKeyDownHandler;

            InitActionTab();
            InitBuyTab();
            InitGameSettingsTab();
            InitAliasController();
            InitExtra();

            // Зададим привязку по умолчанию
            this.StateBinding = EntryStateBinding.Default;
        }

        private void KeyboardKeyDownHandler(object sender, VirtualKeyboard.KeyboardClickRoutedEvtArgs args)
        {
            // Определим новую последовательность
            string key = args.Key.ToLower();

            // Убедимся, что в комбобоксе выделен нужный элемент
            this.iKeyboard.IsSelected = true;

            if (currentKeySequence == null || currentKeySequence.Keys.Length == 2 ||
                currentKeySequence.Keys.Length == 1 && !args.SpecialKeyFlags.HasFlag(VirtualKeyboard.SpecialKey.Shift))
            {
                // Теперь создаем новую последовательность с 1 клавишей
                currentKeySequence = new KeySequence(key);
                this.StateBinding = EntryStateBinding.KeyDown;

                // Отредактируем текст у панелей
                this.keyDownPanelLabel.Text = string.Format(Res.KeyDown1_Format, currentKeySequence[0].ToUpper());
                this.keyReleasePanelLabel.Text = string.Format(Res.KeyUp1_Format, currentKeySequence[0].ToUpper());
            }
            else if (currentKeySequence.Keys.Length == 1)
            {
                // Иначе в последовательности уже есть 1 кнопка и надо добавить вторую
                // Проверяем, что выбрана не та же кнопка
                if (currentKeySequence[0] == key) return;

                currentKeySequence = new KeySequence(currentKeySequence[0], key);
                this.StateBinding = EntryStateBinding.KeyDown;

                string key1Upper = currentKeySequence[0].ToUpper();
                string key2Upper = currentKeySequence[1].ToUpper();

                this.keyDownPanelLabel.Text = string.Format(Res.KeyDown2_Format, key2Upper, key1Upper);
                this.keyReleasePanelLabel.Text = string.Format(Res.KeyUp2_Format, key2Upper, key1Upper);
            }

            ColorizeKeyboard();

            // Обновляем интерфейс под новую последовательность
            this.UpdateAttachmentPanels();
        }

        private void AttachmentsBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Нажатие на панель возможно когда еще не указана последовательность
            if (this.currentKeySequence == null)
                return;

            Border border = (Border)sender;
            bool isKeyDownBinding = ((FrameworkElement)sender).Tag as string == EntryStateBinding.KeyDown.ToString();

            this.StateBinding = isKeyDownBinding ? EntryStateBinding.KeyDown : EntryStateBinding.KeyUp;

            UpdateAttachmentPanels();
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cbox = (ComboBox)e.Source;
            ComboBoxItem selectedItem = (ComboBoxItem)cbox.SelectedItem;

            if (selectedItem.Name == iKeyboard.Name)
            {
                // При выборе клавиатуры по умолчанию не выбрана последовательность
                this.StateBinding = EntryStateBinding.InvalidState;
            }
            else
            {
                EntryStateBinding selectedState = selectedItem.Name == iDefault.Name ?
                    EntryStateBinding.Default :
                    EntryStateBinding.Alias;

                solidAttachmentPanelLabel.Text = selectedState == EntryStateBinding.Default ?
                    Res.CommandsByDefault_Hint :
                    Res.CommandsInAlias_Hint;

                this.currentKeySequence = null;
                this.ColorizeKeyboard();

                if (selectedState == EntryStateBinding.Alias)
                {
                    // И при этом если ни одной команды не создано, то задаем неверное состояние
                    selectedState =
                        aliasPanel.Tag == null ?
                        EntryStateBinding.InvalidState :
                        EntryStateBinding.Alias;
                }

                this.StateBinding = selectedState;
            }

            UpdateAttachmentPanels();
        }

        private void AddAliasButton_Click(object sender, RoutedEventArgs e)
        {
            string aliasName = this.newAliasNameTextbox.Text;
            AddAliasButton(aliasName, new List<Entry>());
        }

        private void DeleteAliasButton_Click(object sender, RoutedEventArgs e)
        {
            this.ResetAttachmentPanels();

            FrameworkElement targetElement = aliasPanel.Tag as FrameworkElement;
            // Отвяжем настройки, привязанные к текущей кнопке
            targetElement.Tag = null;
            // Уберем привязку к тегу панели алиасов
            BindingOperations.ClearAllBindings(targetElement);
            aliasPanel.Children.Remove(targetElement);

            if (aliasPanel.Children.Count > 0)
            {
                // Если в панели еще есть элементы, то задаем новый тег
                // и обновляем панели под новый алиас
                aliasPanel.Tag = aliasPanel.Children[0];
                UpdateAttachmentPanels();

                // Так же для удобства сделаем фокус на первом элементе панели, если такой есть
                if (this.solidAttachmentsPanel.Children.Count > 0)
                {
                    ConfigEntry firstEntry =
                    (ConfigEntry)(this.solidAttachmentsPanel.Children[0] as FrameworkElement).Tag;

                    this.entryUiBindings[firstEntry].Focus();
                }
            }
            else
            {
                // Если удалили последнюю кнопку, то удалим 
                this.RemoveEntry(ConfigEntry.ExtraAliasSet);
                aliasPanel.Tag = null;
                this.StateBinding = EntryStateBinding.InvalidState;
            }
        }

        private void CommandNameTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox box = (TextBox)sender;
            string text = box.Text.Trim();

            // Отключаем кнопку добавления новой команды
            addCmdButton.IsEnabled = false;

            // И включаем только если задана новая команда и её до этого не было
            // TODO: Проверка имени регуляркой
            if (text.Length == 0)
                return;
            else if (customCmdPanel.Children.OfType<ButtonBase>().Any(b => b.Content.ToString() == text))
                return;

            addCmdButton.IsEnabled = true;
        }

        private void AddCmdButton_Click(object sender, RoutedEventArgs e)
        {
            ButtonBase cmdElement = new Chip
            {
                Style = (Style)this.Resources["BubbleButton"],
                Content = cmdTextbox.Text.Trim()
            };
            customCmdPanel.Children.Add(cmdElement);
            customCmdPanel.Tag = cmdElement;

            cmdElement.Click += (_, __) => customCmdPanel.Tag = cmdElement;

            Binding tagBinding = new Binding("Tag")
            {
                Source = customCmdPanel,
                Converter = new TagToFontWeightConverter(),
                ConverterParameter = cmdElement
            };
            cmdElement.SetBinding(ButtonBase.FontWeightProperty, tagBinding);

            AddEntry(ConfigEntry.ExecCustomCmds);
        }

        private void DeleteCmdButton_Click(object sender, RoutedEventArgs e)
        {
            ButtonBase targetButton = customCmdPanel.Tag as ButtonBase;
            BindingOperations.ClearAllBindings(targetButton);
            customCmdPanel.Children.Remove(targetButton);

            if (customCmdPanel.Children.Count > 0)
            {
                customCmdPanel.Tag = customCmdPanel.Children[0];
                this.AddEntry(ConfigEntry.ExecCustomCmds);
            }
            else
            {
                customCmdPanel.Tag = null;
            }
        }

        private void GenerateRandomCrosshairsButton_Click(object sender, RoutedEventArgs e)
        {
            int count = (int)cycleChSlider.Value;
            string prefix = GeneratePrefix();
            Random rnd = new Random();

            string GenerateRandomCmd(ConfigEntry entry, double from, double to, bool asInteger)
            {
                double randomValue;

                if (!asInteger)
                    randomValue = from + rnd.NextDouble() * (to - from);
                else
                    randomValue = from + rnd.NextDouble() * (to - from + 1);

                string formatted = Executable.FormatNumber(randomValue, asInteger);
                return $"{entry.ToString()} {formatted}";
            };

            string firstCrosshair = null;

            for (int i = 0; i < count; i++)
            {
                string crosshairName = $"{prefix}_ch{i + 1}.cfg";
                if (firstCrosshair == null) firstCrosshair = crosshairName;

                string[] lines = new string[]
                {
                    GenerateRandomCmd(ConfigEntry.cl_crosshair_drawoutline, 0, 1, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshair_outlinethickness, 1, 2, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshair_sniper_width, 1, 5, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshairalpha, 200, 255, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshairdot, 0, 1, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshairgap, 0, 1, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshair_t, 0, 1, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshairsize, -5, 5, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshairstyle, 0, 5, true),
                    GenerateRandomCmd(ConfigEntry.cl_crosshairthickness, 1, 3, true)
                };

                if (File.Exists(crosshairName)) File.Delete(crosshairName);
                File.WriteAllLines(crosshairName, lines);
            }
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{firstCrosshair}\"");
        }

        private void OpenCfgButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Config Maker Config (*.cmc)|*.cmc",
                InitialDirectory = GetTargetFolder()
            };

            if (openFileDialog.ShowDialog() == true)
            {
                XmlSerializer cfgSerializer = new XmlSerializer(typeof(ConfigManager));

                FileInfo fi = new FileInfo(openFileDialog.FileName);
                string cfgName = fi.Name.Replace(".cmc", "");
                cfgNameBox.Text = cfgName;

                try
                {
                    using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                    {
                        this.cfgManager = (ConfigManager)cfgSerializer.Deserialize(fs);
                    }
                    UpdateCfgManager();
                }
                catch (Exception ex)
                {
                    HandleException("Файл поврежден", ex);
                }
            }
        }

        private void SaveCfgButton_Click(object sender, RoutedEventArgs e)
        {
            if (cfgNameBox.Text.Trim().Length == 0)
                cfgNameBox.Text = "ConfigMakerCfg";

            string cfgName = cfgNameBox.Text;
            string cfgManagerPath = Path.Combine(GetTargetFolder(), $"{cfgName}.cmc");

            if (File.Exists(cfgManagerPath)) File.Delete(cfgManagerPath);
            using (FileStream fs = File.OpenWrite(cfgManagerPath))
            {
                XmlSerializer ser = new XmlSerializer(typeof(ConfigManager));
                ser.Serialize(fs, this.cfgManager);
            }
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{cfgManagerPath}\"");
        }

        private void VolumeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (volumeStepSlider == null || maxVolumeSlider == null || minVolumeSlider == null)
                return;

            Slider slider = (Slider)sender;

            // Нижняя граница изменилась
            if (slider.Name == minVolumeSlider.Name)
            {
                maxVolumeSlider.Minimum = slider.Value + 0.01;
            }
            else if (slider.Name == maxVolumeSlider.Name)
            {
                // Иначе верхняя граница изменилась
                minVolumeSlider.Maximum = slider.Value - 0.01;
            }
            else
            {
                // Иначе изменился шаг
            }

            // Определим дельту
            double delta = maxVolumeSlider.Value - minVolumeSlider.Value;
            volumeStepSlider.Maximum = delta;

            // Обновим регулировщик в конфиге, если изменение было сделано пользователем
            if ((bool)volumeRegulatorCheckbox.IsChecked)
                this.AddEntry(ConfigEntry.VolumeRegulator);
        }

        private void CfgFolderPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            string path = ((TextBox)sender).Text.Trim();

            if (Directory.Exists(path))
                this.cfgManager.TargetPath = path;
        }

        private void ResetSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            this.currentKeySequence = null;
            this.StateBinding = EntryStateBinding.Default;

            this.ColorizeKeyboard();
            this.UpdateAttachmentPanels();
        }

        private void SearchCmdTextbox(object sender, TextChangedEventArgs e)
        {
            TextBox textbox = (TextBox)sender;
            string input = textbox.Text.Trim().ToLower();

            UIElementCollection elements = settingsTabPanel.Children;

            // Выводим все элементы, если ничего не ищем
            if (input.Length == 0)
            {
                foreach (FrameworkElement element in elements)
                    element.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (FrameworkElement element in elements)
                {
                    if (element.Tag != null)
                    {
                        ConfigEntry cfgEntry = (ConfigEntry)element.Tag;
                        element.Visibility = cfgEntry.ToString().ToLower().Contains(input) ?
                            Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                        element.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow().ShowDialog();
        }
        #endregion

        #region Filling UI with config entry managers
        void InitActionTab()
        {
            //// Локальный метод для подготовки и настройки нового чекбокса-контроллера
            CheckBox PrepareAction(ConfigEntry cfgEntry, string cmd, bool isMeta)
            {
                CheckBox checkbox = new CheckBox
                {
                    Content = Localize(cfgEntry),
                    Tag = cfgEntry
                };

                string tooltip = isMeta ? $"+{cmd}" : $"{cmd}";
                TextBlock tooltipBlock = new TextBlock();
                tooltipBlock.Inlines.Add(tooltip);
                checkbox.ToolTip = tooltipBlock;

                checkbox.Click += HandleEntryClick;
                actionsPanel.Children.Add(checkbox);
                return checkbox;
            }

            // Локальный метод для добавления действий
            void AddAction(ConfigEntry entry, string cmd, bool isMeta)
            {
                CheckBox checkbox = PrepareAction(entry, cmd, isMeta);

                EntryUiBinding entryUiBinding = new EntryUiBinding
                {
                    AttachedCheckbox = checkbox,
                    // генерируем каждый раз новый элемент во избежание замыкания
                    Generate = () =>
                    {
                        return new Entry()
                        {
                            PrimaryKey = entry,
                            Cmd = new SingleCmd(cmd),
                            IsMetaScript = isMeta,
                            Type = EntryType.Static
                        };
                    },
                    UpdateUI = (cfgEntry) => checkbox.IsChecked = true,
                    Focus = () =>
                    {
                        actionTabButton.IsChecked = true;
                        checkbox.Focus();
                    },
                    Restore = () => checkbox.IsChecked = false,
                    HandleState = (state) =>
                    {
                        checkbox.IsEnabled =
                            state != EntryStateBinding.InvalidState
                            && (!isMeta || state == EntryStateBinding.KeyDown);
                    }                    
                };
                entryUiBindings.Add(entry, entryUiBinding);
            };

            // Метод для добавления новой категории.
            void AddActionGroupSeparator(string text)
            {
                TextBlock block = new TextBlock();
                Inline bold = new Bold(new Run(text));
                block.Inlines.Add(bold);

                Border border = new Border
                {
                    Child = block,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                actionsPanel.Children.Add(border);
            };

            AddActionGroupSeparator(Res.CategoryCommonActions);
            AddAction(ConfigEntry.Fire, "attack", true);
            AddAction(ConfigEntry.SecondaryFire, "attack2", true);
            AddAction(ConfigEntry.Reload, "reload", true);
            AddAction(ConfigEntry.Use, "use", true);
            AddAction(ConfigEntry.DropWeapon, "drop", false);

            AddActionGroupSeparator(Res.CategoryMovement);
            AddAction(ConfigEntry.Forward, "forward", true);
            AddAction(ConfigEntry.Back, "back", true);
            AddAction(ConfigEntry.Moveleft, "moveleft", true);
            AddAction(ConfigEntry.Moveright, "moveright", true);
            AddAction(ConfigEntry.Jump, "jump", true);
            AddAction(ConfigEntry.Duck, "duck", true);
            AddAction(ConfigEntry.Speed, "speed", true);

            AddActionGroupSeparator(Res.CategoryEquipment);
            AddAction(ConfigEntry.PrimaryWeapon, "slot1", false);
            AddAction(ConfigEntry.SecondaryWeapon, "slot2", false);
            AddAction(ConfigEntry.Knife, "slot3", false);
            AddAction(ConfigEntry.CycleGrenades, "slot4", false);
            AddAction(ConfigEntry.Bomb, "slot5", false);
            AddAction(ConfigEntry.HEGrenade, "slot6", false);
            AddAction(ConfigEntry.Flashbang, "slot7", false);
            AddAction(ConfigEntry.Smokegrenade, "slot8", false);
            AddAction(ConfigEntry.DecoyGrenade, "slot9", false);
            AddAction(ConfigEntry.Molotov, "slot10", false);
            AddAction(ConfigEntry.Zeus, "slot11", false);
            AddAction(ConfigEntry.SelectPreviousWeapon, "invprev", false);
            AddAction(ConfigEntry.SelectNextWeapon, "invnext", false);
            AddAction(ConfigEntry.LastWeaponUsed, "lastinv", false);
            AddAction(ConfigEntry.InspectWeapon, "lookatweapon", true);
            AddAction(ConfigEntry.BuyMenu, "buymenu", false);
            AddAction(ConfigEntry.AutoBuy, "autobuy", false);
            AddAction(ConfigEntry.Rebuy, "rebuy", false);
            AddAction(ConfigEntry.ShowTeamEquipment, "cl_show_team_equipment", true);
            AddAction(ConfigEntry.ToggleInventoryDisplay, "show_loadout_toggle", true);

            AddActionGroupSeparator(Res.CategoryCommunication);
            AddAction(ConfigEntry.Microphone, "voicerecord", true);
            AddAction(ConfigEntry.CommandRadio, "radio1", false);
            AddAction(ConfigEntry.StandartRadio, "radio2", false);
            AddAction(ConfigEntry.ReportRadio, "radio3", false);
            AddAction(ConfigEntry.TeamMessage, "messagemode2", false);
            AddAction(ConfigEntry.ChatMessage, "messagemode", false);

            AddActionGroupSeparator(Res.CategoryWarmup);
            AddAction(ConfigEntry.God, "god", false);
            AddAction(ConfigEntry.Noclip, "noclip", false);
            AddAction(ConfigEntry.Impulse101, "impulse 101", false);

            AddActionGroupSeparator(Res.CategoryOther);
            AddAction(ConfigEntry.GraffitiMenu, "spray_menu", true);
            AddAction(ConfigEntry.Cleardecals, "r_cleardecals", false);
            AddAction(ConfigEntry.Scoreboard, "showscores", true);
            AddAction(ConfigEntry.CallVote, "callvote", false);
            AddAction(ConfigEntry.ChooseTeam, "teammenu", false);
            AddAction(ConfigEntry.ToggleConsole, "toggleconsole", false);
            AddAction(ConfigEntry.Clear, "clear", false);



            // jumpthrow script
            string jumpthrowName = "jumpthrow";
            CheckBox jumpthrowCheckbox = PrepareAction(ConfigEntry.Jumpthrow, jumpthrowName, true);

            EntryUiBinding jumpthrowBinding = new EntryUiBinding()
            {
                AttachedCheckbox = jumpthrowCheckbox,
                Focus = () =>
                {
                    actionTabButton.IsChecked = true;
                    jumpthrowCheckbox.Focus();
                },
                Generate = () =>
                {
                    MetaCmd metaCmd = new MetaCmd(
                        jumpthrowName,
                        Executable.SplitCommands("+jump; -attack; -attack2"),
                        Executable.SplitCommands("-jump"));

                    return new Entry()
                    {
                        PrimaryKey = ConfigEntry.Jumpthrow,
                        Cmd = new SingleCmd(jumpthrowName),
                        IsMetaScript = true,
                        Type = EntryType.Static,
                        Dependencies = metaCmd
                    };
                },
                UpdateUI = (entry) => jumpthrowCheckbox.IsChecked = true,
                Restore = () => jumpthrowCheckbox.IsChecked = false,
                HandleState = (state) => jumpthrowCheckbox.IsEnabled = state == EntryStateBinding.KeyDown
            };
            this.entryUiBindings.Add(ConfigEntry.Jumpthrow, jumpthrowBinding);

            // DisplayDamageOn
            string displayDamageOn = "displaydamage_on";
            CheckBox displayDamageOnCheckbox = PrepareAction(ConfigEntry.DisplayDamageOn, displayDamageOn, false);

            this.entryUiBindings.Add(
                ConfigEntry.DisplayDamageOn,
                new EntryUiBinding()
                {
                    AttachedCheckbox = displayDamageOnCheckbox,
                    Focus = () =>
                    {
                        actionTabButton.IsChecked = true;
                        displayDamageOnCheckbox.Focus();
                    },
                    Generate = () =>
                    {
                        string[] stringCmds = new string[]
                        {
                            "developer 1",
                            "con_filter_text \"Damage Given\"",
                            "con_filter_text_out \"Player:\"",
                            "con_filter_enable 2",
                            "echo \"Display damage: On!\""
                        };
                        CommandCollection cmds = new CommandCollection(
                            stringCmds.Select(cmd => new SingleCmd(cmd)));

                        return new Entry()
                        {
                            PrimaryKey = ConfigEntry.DisplayDamageOn,
                            Cmd = new SingleCmd(displayDamageOn),
                            IsMetaScript = false,
                            Type = EntryType.Static,
                            Dependencies = new CommandCollection(new AliasCmd(displayDamageOn, cmds))
                        };
                    },
                    UpdateUI = (entry) => displayDamageOnCheckbox.IsChecked = true,
                    Restore = () => displayDamageOnCheckbox.IsChecked = false,
                    HandleState = (state) => displayDamageOnCheckbox.IsEnabled = state != EntryStateBinding.InvalidState
                });

            // DisplayDamageOff
            string displayDamageOff = "displaydamage_off";
            CheckBox displayDamageOffCheckbox = PrepareAction(ConfigEntry.DisplayDamageOff, displayDamageOff, false);

            this.entryUiBindings.Add(
                ConfigEntry.DisplayDamageOff,
                new EntryUiBinding()
                {
                    AttachedCheckbox = displayDamageOffCheckbox,
                    Focus = () =>
                    {
                        actionTabButton.IsChecked = true;
                        displayDamageOffCheckbox.Focus();
                    },
                    Generate = () =>
                    {
                        string[] stringCmds = new string[]
                        {
                            "con_filter_enable 0",
                            "developer 0",
                            "echo \"Display damage: Off!\""
                        };
                        CommandCollection cmds = new CommandCollection(
                            stringCmds.Select(cmd => new SingleCmd(cmd)));

                        return new Entry()
                        {
                            PrimaryKey = ConfigEntry.DisplayDamageOff,
                            Cmd = new SingleCmd(displayDamageOff),
                            IsMetaScript = false,
                            Type = EntryType.Static,
                            Dependencies = new CommandCollection(new AliasCmd(displayDamageOff, cmds))
                        };
                    },
                    UpdateUI = (entry) => displayDamageOffCheckbox.IsChecked = true,
                    Restore = () => displayDamageOffCheckbox.IsChecked = false,
                    HandleState = (state) => displayDamageOffCheckbox.IsEnabled = state != EntryStateBinding.InvalidState
                });
        }

        void InitBuyTab()
        {
            // Добавляем главный чекбокс
            CheckBox mainCheckbox = new CheckBox
            {
                Content = Localize(ConfigEntry.BuyScenario),
                Tag = ConfigEntry.BuyScenario
            };
            mainCheckbox.Click += HandleEntryClick;
            buyTabStackPanel.Children.Add(mainCheckbox);

            // Панель на которой будут располагаться все элементы для закупки
            WrapPanel buyPanel = new WrapPanel();
            buyTabStackPanel.Children.Add(buyPanel);

            // Свяжем свойство активности с чекбоксом
            Binding enabledBinding = new Binding("IsChecked")
            {
                Source = mainCheckbox
            };
            buyPanel.SetBinding(WrapPanel.IsEnabledProperty, enabledBinding);


            // Локальный метод для получения всех чекбоксов с оружием
            List<CheckBox> GetWeaponCheckboxes()
            {
                return buyPanel.Children.OfType<StackPanel>()
                .SelectMany(s => s.Children.OfType<CheckBox>()).ToList();
            };

            // Обработчик интерфейса настроек закупки
            EntryUiBinding buyEntryBinding = new EntryUiBinding()
            {
                AttachedCheckbox = mainCheckbox,
                Focus = () => buyTabButton.IsChecked = true,
                UpdateUI = (entry) =>
                {
                    IParametrizedEntry<string[]> extendedEntry = (IParametrizedEntry<string[]>)entry;
                    string[] weapons = extendedEntry.Arg;

                    // зададим состояние чекбоксов согласно аргументам
                    GetWeaponCheckboxes().ForEach(c => c.IsChecked = weapons.Contains(((string)c.Tag)));
                    // не забываем про главный чекбокс
                    mainCheckbox.IsChecked = true;
                },
                Generate = () =>
                {
                    string[] weaponsToBuy = GetWeaponCheckboxes()
                    .Where(c => (bool)c.IsChecked).Select(c => (string)c.Tag).ToArray();

                    List<SingleCmd> buyCmds = weaponsToBuy.Select(weapon => new SingleCmd($"buy {weapon}")).ToList();

                    AliasCmd buyAlias = new AliasCmd($"{GeneratePrefix()}_buyscenario", buyCmds);

                    return new ParametrizedEntry<string[]>()
                    {
                        PrimaryKey = ConfigEntry.BuyScenario,
                        Type = EntryType.Dynamic,
                        Dependencies = new CommandCollection(buyAlias),
                        Cmd = buyAlias.Name,
                        Arg = weaponsToBuy,
                        IsMetaScript = false
                    };
                },
                Restore = () =>
                {
                    mainCheckbox.IsChecked = false;
                    GetWeaponCheckboxes().ForEach(c => c.IsChecked = false);
                },
                HandleState = (state) => mainCheckbox.IsEnabled = state != EntryStateBinding.InvalidState
            };
            // Добавляем обработчика
            this.entryUiBindings.Add(ConfigEntry.BuyScenario, buyEntryBinding);

            StackPanel currentPanel = null;

            void AddWeapon(string weaponId, string localizedName)
            {
                CheckBox weaponCheckbox = new CheckBox
                {
                    Content = localizedName,
                    Tag = weaponId
                };

                // При нажатии на чекбокс оружия искусственно вызовем событие обработки нажатия на главный чекбокс
                weaponCheckbox.Click += (_, __) =>
                {
                    this.AddEntry(ConfigEntry.BuyScenario);
                };

                currentPanel.Children.Add(weaponCheckbox);
            };

            // Метод для добавления новой категории. Определяет новый stackpanel и создает текстовую метку
            void AddGroupSeparator(string text)
            {
                currentPanel = new StackPanel();
                buyPanel.Children.Add(currentPanel);

                TextBlock block = new TextBlock();
                Inline bold = new Bold(new Run(text));
                block.Inlines.Add(bold);

                Border border = new Border
                {
                    Child = block,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                currentPanel.Children.Add(border);
            };

            AddGroupSeparator(Res.Pistols);
            AddWeapon("glock", Res.Pistol1);
            AddWeapon("elite", Res.Pistol2);
            AddWeapon("p250", Res.Pistol3);
            AddWeapon("fn57", Res.Pistol4);
            AddWeapon("deagle", Res.Pistol5);

            AddGroupSeparator(Res.Heavy);
            AddWeapon("nova", Res.Heavy1);
            AddWeapon("xm1014", Res.Heavy2);
            AddWeapon("mag7", Res.Heavy3);
            AddWeapon("m249", Res.Heavy4);
            AddWeapon("negev", Res.Heavy5);

            AddGroupSeparator(Res.SMGs);
            AddWeapon("mac10", Res.Smg1);
            AddWeapon("mp7", Res.Smg2);
            AddWeapon("ump45", Res.Smg3);
            AddWeapon("p90", Res.Smg4);
            AddWeapon("bizon", Res.Smg5);

            AddGroupSeparator(Res.Rifles);
            AddWeapon("famas", Res.Rifle1);
            AddWeapon("ak47", Res.Rifle2);
            AddWeapon("ssg08", Res.Rifle3);
            AddWeapon("aug", Res.Rifle4);
            AddWeapon("awp", Res.Rifle5);
            AddWeapon("g3sg1", Res.Rifle6);

            AddGroupSeparator(Res.Gear);
            AddWeapon("vest", Res.Gear1);
            AddWeapon("vesthelm", Res.Gear2);
            AddWeapon("taser", Res.Gear3);

            AddGroupSeparator(Res.Grenades);
            AddWeapon("molotov", Res.Grenade1);
            AddWeapon("decoy", Res.Grenade2);
            AddWeapon("flashbang", Res.Grenade3);
            AddWeapon("hegrenade", Res.Grenade4);
            AddWeapon("smokegrenade", Res.Grenade5);
        }
        
        void InitGameSettingsTab()
        {
            double rowHeight = 30;

            Tuple<TextBlock, Grid, Button, CheckBox> PrepareNewRow(ConfigEntry cfgEntry, bool needToggle)
            {
                Grid cmdControllerGrid = new Grid
                {
                    Height = rowHeight
                };

                cmdControllerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(30, GridUnitType.Star) });
                cmdControllerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(60, GridUnitType.Star) });
                cmdControllerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(10, GridUnitType.Star) });
                cmdControllerGrid.Tag = cfgEntry;

                // Текст с результирующей командой
                TextBlock resultCmd = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                cmdControllerGrid.Children.Add(resultCmd);
                Grid.SetColumn(resultCmd, 0);

                // Сетка с управляющими элементами
                Grid mainControlGrid = new Grid();
                cmdControllerGrid.Children.Add(mainControlGrid);
                Grid.SetColumn(mainControlGrid, 1);

                Grid customControlsGrid = new Grid();
                mainControlGrid.Children.Add(customControlsGrid);

                // Определяем нужна ли кнопка для циклических аргументов
                Button toggleButton = null;

                if (needToggle)
                {
                    mainControlGrid.ColumnDefinitions.Add(
                        new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    mainControlGrid.ColumnDefinitions.Add(
                        new ColumnDefinition() { Width = new GridLength(-1, GridUnitType.Auto) });

                    toggleButton = new Button
                    {
                        Style = (Style)this.FindResource("MaterialDesignFlatButton"),
                        Content = "⇄"
                    };

                    mainControlGrid.Children.Add(toggleButton);
                    Grid.SetColumn(toggleButton, 1);
                }

                // Колонка с чекбоксом
                CheckBox checkbox = new CheckBox
                {
                    Tag = cfgEntry,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                cmdControllerGrid.Children.Add(checkbox);
                Grid.SetColumn(checkbox, 2);
                checkbox.Click += HandleEntryClick;

                // Привяжем доступность регулирующих элементов к значению чекбокса
                Binding checkedBinding = new Binding("IsChecked")
                {
                    Source = checkbox
                };
                customControlsGrid.SetBinding(Grid.IsEnabledProperty, checkedBinding);
                // А так же привяжем отдельно наш ToggleTool, т.к. он находится в общем гриде

                settingsTabPanel.Children.Add(cmdControllerGrid);

                return new Tuple<TextBlock, Grid, Button, CheckBox>(resultCmd, customControlsGrid, toggleButton, checkbox);
            };

            
            void AddIntervalCmdController(ConfigEntry cfgEntry, double from, double to, double step, double defaultValue)
            {
                var tuple = PrepareNewRow(cfgEntry, true);
                TextBlock resultCmdBlock = tuple.Item1;
                Grid sliderGrid = tuple.Item2;
                Button toggleButton = tuple.Item3;
                CheckBox checkbox = tuple.Item4;

                bool isInteger = from % 1 == 0 && to % 1 == 0 && step % 1 == 0;

                // Колонка с ползунком
                sliderGrid.ColumnDefinitions.Add(new ColumnDefinition());
                sliderGrid.ColumnDefinitions.Add(new ColumnDefinition());
                sliderGrid.ColumnDefinitions.Add(new ColumnDefinition());
                sliderGrid.ColumnDefinitions[0].Width = new GridLength(-1, GridUnitType.Auto);
                sliderGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                sliderGrid.ColumnDefinitions[2].Width = new GridLength(-1, GridUnitType.Auto);

                sliderGrid.MaxHeight = rowHeight;
                Slider slider = new Slider
                {
                    Margin = new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Minimum = from,
                    Maximum = to
                };

                sliderGrid.Children.Add(slider);
                Grid.SetColumn(slider, 1);

                Border minBorder = new Border();
                //minBorder.Width = 20;
                TextBlock minText = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                //minText.FontSize = 12;
                minText.Inlines.Add(Executable.FormatNumber(from, from % 1 == 0));
                minBorder.Child = minText;
                sliderGrid.Children.Add(minBorder);

                Border maxBorder = new Border();
                //maxBorder.Width = 20;
                TextBlock maxText = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                //maxText.FontSize = 11;
                maxText.Inlines.Add(Executable.FormatNumber(to, to % 1 == 0));
                maxBorder.Child = maxText;
                sliderGrid.Children.Add(maxBorder);
                Grid.SetColumn(maxBorder, 2);

                toggleButton.Click += (_, __) =>
                {
                    ToggleWindow toggleWindow = new ToggleWindow(isInteger, from, to);
                    if ((bool)toggleWindow.ShowDialog())
                    {
                        double[] values = toggleWindow.GeneratedArg.Split(' ').Select(value =>
                        {
                            Executable.TryParseDouble(value, out double parsedValue);
                            return parsedValue;
                        }).ToArray();

                        resultCmdBlock.Text = Executable.GenerateToggleCmd(cfgEntry.ToString(), values, isInteger);
                        // Сохраним аргумент в теге
                        resultCmdBlock.Tag = values;

                        if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                            this.AddEntry(cfgEntry);
                    }
                    else
                    {

                    }
                };

                slider.IsSnapToTickEnabled = true;
                slider.TickFrequency = step;

                slider.ValueChanged += (obj, args) =>
                {
                    double value = args.NewValue;
                    string formatted = Executable.FormatNumber(args.NewValue, isInteger);
                    Executable.TryParseDouble(formatted, out double fixedValue);
                    fixedValue = isInteger ? ((int)fixedValue) : fixedValue;

                    resultCmdBlock.Text = $"{cfgEntry.ToString()} {formatted}";
                    resultCmdBlock.Tag = fixedValue;

                    if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                        this.AddEntry(cfgEntry);
                };

                // обработчик интерфейса
                EntryUiBinding entryBinding = new EntryUiBinding()
                {
                    AttachedCheckbox = checkbox,
                    Focus = () =>
                    {
                        gameSettingsTabButton.IsChecked = true;
                        checkbox.Focus();
                    },
                    Restore = () =>
                    {
                        // Сперва сбрасываем чекбокс, это важно
                        checkbox.IsChecked = false;
                        slider.Value = defaultValue;
                        resultCmdBlock.Tag = defaultValue;
                    },
                    Generate = () =>
                    {
                        if (resultCmdBlock.Tag is double)
                        {
                            return new ParametrizedEntry<double>()
                            {
                                PrimaryKey = cfgEntry,
                                Cmd = new SingleCmd(resultCmdBlock.Text),
                                IsMetaScript = false,
                                Type = EntryType.Dynamic,
                                Arg = (double)resultCmdBlock.Tag
                            };
                        }
                        else
                        {
                            return new ParametrizedEntry<double[]>()
                            {
                                PrimaryKey = cfgEntry,
                                Cmd = new SingleCmd(resultCmdBlock.Text),
                                IsMetaScript = false,
                                Type = EntryType.Dynamic,
                                Arg = (double[])resultCmdBlock.Tag
                            };
                        }
                    },
                    UpdateUI = (entry) =>
                    {
                        checkbox.IsChecked = true;
                        if (entry is IParametrizedEntry<double>)
                        {
                            IParametrizedEntry<double> extendedEntry = (IParametrizedEntry<double>)entry;
                            slider.Value = extendedEntry.Arg;
                            resultCmdBlock.Tag = extendedEntry.Arg;
                        }
                        else
                        {
                            IParametrizedEntry<double[]> extendedEntry = (IParametrizedEntry<double[]>)entry;
                            resultCmdBlock.Text = Executable.GenerateToggleCmd(
                                cfgEntry.ToString(), extendedEntry.Arg, isInteger);
                            resultCmdBlock.Tag = extendedEntry.Arg;
                        }
                    },
                    HandleState = (state) => checkbox.IsEnabled = state != EntryStateBinding.InvalidState
                };
                this.entryUiBindings.Add(cfgEntry, entryBinding);

                // Задаем начальное значение и тут же подключаем обработчика интерфейса
                slider.Value = defaultValue;
            };
            
            void AddComboboxCmdController(ConfigEntry cfgEntry, string[] names, int defaultIndex, bool isIntegerArg)
            {
                var tuple = PrepareNewRow(cfgEntry, true);
                TextBlock resultCmdBlock = tuple.Item1;
                Grid comboboxGrid = tuple.Item2;
                Button toggleButton = tuple.Item3;
                CheckBox checkbox = tuple.Item4;

                // Если аргумент - число, то создадим сетку с 2-мя кнопками
                //Grid comboboxGrid = new Grid();
                comboboxGrid.ColumnDefinitions.Add(new ColumnDefinition());

                ComboBox combobox = new ComboBox
                {
                    MaxHeight = rowHeight
                };
                comboboxGrid.Children.Add(combobox);
                ComboBoxAssist.SetClassicMode(combobox, true);

                // Если надо предусмотреть функцию toggle, то расширяем сетку и добавляем кнопку
                if (isIntegerArg)
                {
                    toggleButton.Click += (_, __) =>
                    {
                        ToggleWindow toggleWindow = new ToggleWindow(true, 0, names.Length - 1);
                        if ((bool)toggleWindow.ShowDialog())
                        {
                            int[] values = toggleWindow.GeneratedArg.Split(' ').Select(n => int.Parse(n)).ToArray();
                            resultCmdBlock.Text = Executable.GenerateToggleCmd(cfgEntry.ToString(), values);
                            // Сохраним аргумент в теге
                            resultCmdBlock.Tag = values;

                            if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                                this.AddEntry(cfgEntry);
                        }
                        else
                        {

                        }
                    };
                }

                // Зададим элементы комбобокса
                names.ToList().ForEach(name => combobox.Items.Add(name));

                // Создадим обработчика пораньше, т.к. он понадобится уже при задании начального индекса комбобокса
                EntryUiBinding entryBinding = new EntryUiBinding()
                {
                    AttachedCheckbox = checkbox,
                    Focus = () =>
                    {
                        gameSettingsTabButton.IsChecked = true;
                        checkbox.Focus();
                    },
                    Restore = () =>
                    {
                        // Сначала сбрасываем чекбокс, ибо дальше мы с ним сверяемся
                        checkbox.IsChecked = false;
                        // искусственно сбрасываем выделенный элемент
                        combobox.SelectedIndex = -1; 
                        // и гарантированно вызываем обработчик SelectedIndexChanged
                        combobox.SelectedIndex = defaultIndex; 
                    },
                    Generate = () =>
                    {
                        SingleCmd resultCmd = new SingleCmd(resultCmdBlock.Text);

                        if (resultCmdBlock.Tag is int)
                        {
                            return new ParametrizedEntry<int>()
                            {
                                PrimaryKey = cfgEntry,
                                Type = EntryType.Dynamic,
                                IsMetaScript = false,
                                Cmd = resultCmd,
                                Arg = (int)resultCmdBlock.Tag
                            };
                        }
                        else
                        {
                            return new ParametrizedEntry<int[]>()
                            {
                                PrimaryKey = cfgEntry,
                                Type = EntryType.Dynamic,
                                IsMetaScript = false,
                                Cmd = resultCmd,
                                Arg = (int[])resultCmdBlock.Tag
                            };
                        }
                    },
                    UpdateUI = (entry) =>
                    {
                        checkbox.IsChecked = true;

                        if (entry is IParametrizedEntry<int>)
                        {
                            IParametrizedEntry<int> extendedEntry = (IParametrizedEntry<int>)entry;
                            combobox.SelectedIndex = extendedEntry.Arg;
                            resultCmdBlock.Tag = extendedEntry.Arg;
                        }
                        else
                        {
                            IParametrizedEntry<int[]> extendedEntry = (IParametrizedEntry<int[]>)entry;
                            resultCmdBlock.Text = Executable.GenerateToggleCmd(cfgEntry.ToString(), extendedEntry.Arg);
                            resultCmdBlock.Tag = extendedEntry.Arg;
                        }
                    },
                    HandleState = (state) => checkbox.IsEnabled = state != EntryStateBinding.InvalidState
                };
                // Добавим его в словарь
                this.entryUiBindings.Add(cfgEntry, entryBinding);

                combobox.SelectionChanged += (obj, args) =>
                {
                    if (combobox.SelectedIndex == -1) return;

                    string resultCmdStr;
                    if (isIntegerArg)
                        resultCmdStr = $"{cfgEntry.ToString()} {combobox.SelectedIndex}";
                    else
                        resultCmdStr = $"{cfgEntry.ToString()} {combobox.SelectedItem}";

                    resultCmdBlock.Text = resultCmdStr;
                    resultCmdBlock.Tag = combobox.SelectedIndex;
                    if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                        this.AddEntry(cfgEntry);
                };
                
                // Команда по умолчанию обновится, т.к. уже есть обработчик
                combobox.SelectedIndex = defaultIndex;                
            };

            void addTextboxNumberCmdController(ConfigEntry cfgEntry, double defaultValue, bool asInteger)
            {
                var tuple = PrepareNewRow(cfgEntry, true);
                TextBlock resultCmdBlock = tuple.Item1;
                Grid textboxGrid = tuple.Item2;
                Button toggleButton = tuple.Item3;
                CheckBox checkbox = tuple.Item4;
                string fixedDefaultStrValue = Executable.FormatNumber(defaultValue, asInteger);
                double fixedDefaultValue = double.Parse(fixedDefaultStrValue, CultureInfo.InvariantCulture);

                //Grid textboxGrid = new Grid();

                TextBox textbox = new TextBox
                {
                    MaxHeight = rowHeight
                };
                textboxGrid.Children.Add(textbox);

                toggleButton.Click += (_, __) =>
                {
                    ToggleWindow toggleWindow = new ToggleWindow(asInteger, double.MinValue, double.MaxValue);
                    if ((bool)toggleWindow.ShowDialog())
                    {
                        double[] values = toggleWindow.GeneratedArg.Split(' ').Select(value =>
                        {
                            Executable.TryParseDouble(value, out double parsedValue);
                            return parsedValue;
                        }).ToArray();

                        resultCmdBlock.Text = Executable.GenerateToggleCmd(cfgEntry.ToString(), values, asInteger);
                        // Сохраним аргумент в теге
                        resultCmdBlock.Tag = values;

                        if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                            this.AddEntry(cfgEntry);
                    }
                    else
                    {

                    }
                };

                textbox.TextChanged += (obj, args) =>
                {
                    if (!Executable.TryParseDouble(textbox.Text.Trim(), out double fixedValue))
                        return;
                    // Обрезаем дробную часть, если необходимо
                    fixedValue = asInteger? (int)fixedValue: fixedValue;

                    // сохраним последнее верное значение в тег текстового блока
                    resultCmdBlock.Tag = fixedValue;

                    string formatted = Executable.FormatNumber(fixedValue, asInteger);
                    resultCmdBlock.Text = $"{cfgEntry.ToString()} {formatted}";

                    if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                        AddEntry(cfgEntry);
                };

                EntryUiBinding entryBinding = new EntryUiBinding()
                {
                    AttachedCheckbox = checkbox,
                    Focus = () =>
                    {
                        gameSettingsTabButton.IsChecked = true;
                        checkbox.Focus();
                    },
                    Restore = () =>
                    {
                        checkbox.IsChecked = false;
                        textbox.Text = fixedDefaultStrValue;
                        resultCmdBlock.Tag = fixedDefaultValue;
                    },
                    Generate = () =>
                    {
                        SingleCmd generatedCmd = new SingleCmd(resultCmdBlock.Text);

                        if (resultCmdBlock.Tag is double)
                        {
                            return new ParametrizedEntry<double>()
                            {
                                PrimaryKey = cfgEntry,
                                Cmd = generatedCmd,
                                Type = EntryType.Dynamic,
                                IsMetaScript = false,
                                Arg = (double)resultCmdBlock.Tag // Подтягиваем аргумент из тега
                            };
                        }
                        else
                        {
                            return new ParametrizedEntry<double[]>()
                            {
                                PrimaryKey = cfgEntry,
                                Cmd = generatedCmd,
                                Type = EntryType.Dynamic,
                                IsMetaScript = false,
                                Arg = (double[])resultCmdBlock.Tag // Подтягиваем аргумент из тега
                            };
                        }
                    },
                    UpdateUI = (entry) =>
                    {
                        checkbox.IsChecked = true;

                        if (entry is IParametrizedEntry<double>)
                        {
                            IParametrizedEntry<double> extendedEntry = (IParametrizedEntry<double>)entry;
                            textbox.Text = Executable.FormatNumber(extendedEntry.Arg, asInteger);
                            resultCmdBlock.Tag = extendedEntry.Arg;
                        }
                        else
                        {
                            IParametrizedEntry<double[]> extendedEntry = (IParametrizedEntry<double[]>)entry;
                            double[] values = extendedEntry.Arg;

                            resultCmdBlock.Text = Executable.GenerateToggleCmd(cfgEntry.ToString(), values, asInteger);
                            resultCmdBlock.Tag = extendedEntry.Arg;                            
                        }                        
                    },
                    HandleState = (state) => checkbox.IsEnabled = state != EntryStateBinding.InvalidState
                };
                this.entryUiBindings.Add(cfgEntry, entryBinding);

                // Начальное значение
                textbox.Text = fixedDefaultStrValue;
            };

            void addTextboxStringCmdController(ConfigEntry cfgEntry, string defaultValue)
            {
                var tuple = PrepareNewRow(cfgEntry, false);
                TextBlock resultCmdBlock = tuple.Item1;
                Grid textBoxGrid = tuple.Item2;
                CheckBox checkbox = tuple.Item4;

                TextBox textbox = new TextBox
                {
                    MaxHeight = rowHeight
                };

                textBoxGrid.Children.Add(textbox);

                textbox.TextChanged += (obj, args) =>
                {
                    string text = textbox.Text;
                    bool wrap = text.Contains(" ");

                    resultCmdBlock.Text = $"{cfgEntry.ToString()} {(wrap?"\"":"")}{text}{(wrap ? "\"" : "")}";

                    if ((bool)checkbox.IsChecked) // Добавляем в конфиг только если это сделал сам пользователь
                        AddEntry(cfgEntry);
                };

                EntryUiBinding entryBinding = new EntryUiBinding()
                {
                    AttachedCheckbox = checkbox,
                    Focus = () =>
                    {
                        gameSettingsTabButton.IsChecked = true;
                        checkbox.Focus();
                    },
                    Restore = () =>
                    {
                        checkbox.IsChecked = false;
                        textbox.Text = defaultValue;
                    },
                    Generate = () =>
                    {
                        SingleCmd generatedCmd = new SingleCmd(resultCmdBlock.Text);

                        return new ParametrizedEntry<string>()
                        {
                            PrimaryKey = cfgEntry,
                            Cmd = generatedCmd,
                            Type = EntryType.Dynamic,
                            IsMetaScript = false,
                            Arg = (string)textbox.Text // Подтягиваем аргумент из тега
                        };
                    },
                    UpdateUI = (entry) =>
                    {
                        checkbox.IsChecked = true;
                        IParametrizedEntry<string> extendedEntry = (IParametrizedEntry<string>)entry;
                        textbox.Text = extendedEntry.Arg;
                    },
                    HandleState = (state) => checkbox.IsEnabled = state != EntryStateBinding.InvalidState
                };
                this.entryUiBindings.Add(cfgEntry, entryBinding);

                // Начальное значение
                textbox.Text = defaultValue;
            };

            void addGroupHeader(string text)
            {
                TextBlock block = new TextBlock();
                block.Inlines.Add(new Bold(new Run(text)));
                block.HorizontalAlignment = HorizontalAlignment.Center;
                block.VerticalAlignment = VerticalAlignment.Center;
                settingsTabPanel.Children.Add(block);
            };

            string[] toggleStrings = new string[] { Res.Off, Res.On };
            
            addGroupHeader(Res.CategoryMouseSettings);
            addTextboxNumberCmdController(ConfigEntry.sensitivity, 2.5, false);
            addTextboxNumberCmdController(ConfigEntry.zoom_sensitivity_ratio_mouse, 1, false);
            AddComboboxCmdController(ConfigEntry.m_rawinput, toggleStrings, 1, true);
            AddComboboxCmdController(ConfigEntry.m_customaccel, toggleStrings, 0, true);
            AddIntervalCmdController(ConfigEntry.m_customaccel_exponent, 0.05, 10, 0.05, 1.05);

            addGroupHeader(Res.CategoryClientCommands);
            AddComboboxCmdController(ConfigEntry.cl_autowepswitch, toggleStrings, 1, true);
            AddIntervalCmdController(ConfigEntry.cl_bob_lower_amt, 5, 30, 1, 21);
            AddIntervalCmdController(ConfigEntry.cl_bobamt_lat, 0.1, 2, 0.1, 0.4);
            AddIntervalCmdController(ConfigEntry.cl_bobamt_vert, 0.1, 2, 0.1, 0.25);
            AddIntervalCmdController(ConfigEntry.cl_bobcycle, 0.1, 2, 0.01, 0.98);
            addTextboxNumberCmdController(ConfigEntry.cl_clanid, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_color, new string[] { Res.Yellow, Res.Purple, Res.Green, Res.Blue, Res.Orange }, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_dm_buyrandomweapons, toggleStrings, 1, true);
            AddComboboxCmdController(ConfigEntry.cl_draw_only_deathnotices, toggleStrings, 1, true);
            AddComboboxCmdController(ConfigEntry.cl_hud_color,
                new string[] { Res.Default, Res.White, Res.LightBlue, Res.Blue, Res.Purple, Res.Pink, Res.Red, Res.Orange, Res.Yellow, Res.Green, Res.Aqua }, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_hud_healthammo_style, new string[] { Res.Health_Style0, Res.Health_Style1 }, 0, true);
            AddIntervalCmdController(ConfigEntry.cl_hud_radar_scale, 0.8, 1.3, 0.1, 1);
            AddIntervalCmdController(ConfigEntry.cl_hud_background_alpha, 0, 1, 0.1, 1);
            AddComboboxCmdController(ConfigEntry.cl_hud_playercount_pos, new string[] { Res.Top, Res.Bottom }, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_hud_playercount_showcount, new string[] { Res.ShowAvatars, Res.ShowCount}, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_loadout_colorweaponnames, toggleStrings, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_mute_enemy_team, toggleStrings, 0, true);
            addTextboxNumberCmdController(ConfigEntry.cl_pdump, -1, true);
            AddComboboxCmdController(ConfigEntry.cl_radar_always_centered, toggleStrings, 0, true);
            AddIntervalCmdController(ConfigEntry.cl_radar_icon_scale_min, 0.4, 1, 0.01, 0.7);
            AddIntervalCmdController(ConfigEntry.cl_radar_scale, 0.25, 1, 0.01, 0.7);
            AddComboboxCmdController(ConfigEntry.cl_righthand, toggleStrings, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_showfps, toggleStrings, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_show_clan_in_death_notice, toggleStrings, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_showpos, toggleStrings, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_teammate_colors_show, toggleStrings, 0, true);
            AddComboboxCmdController(ConfigEntry.cl_teamid_overhead_always, toggleStrings, 0, true);
            AddIntervalCmdController(ConfigEntry.cl_teamid_overhead_name_alpha, 0, 255, 1, 100);
            AddIntervalCmdController(ConfigEntry.cl_timeout, 4, 30, 1, 30);
            AddComboboxCmdController(ConfigEntry.cl_use_opens_buy_menu, toggleStrings, 1, true);
            AddIntervalCmdController(ConfigEntry.cl_viewmodel_shift_left_amt, 0.5, 2, 0.05, 1.5);
            AddIntervalCmdController(ConfigEntry.cl_viewmodel_shift_right_amt, 0.25, 2, 0.05, 0.75);

            addGroupHeader(Res.CategoryCrosshair);
            AddComboboxCmdController(ConfigEntry.cl_crosshair_drawoutline, toggleStrings, 0, true);
            AddIntervalCmdController(ConfigEntry.cl_crosshair_dynamic_maxdist_splitratio, 0, 1, 0.1, 0.35);
            AddIntervalCmdController(ConfigEntry.cl_crosshair_dynamic_splitalpha_innermod, 0, 1, 0.01, 1);
            AddIntervalCmdController(ConfigEntry.cl_crosshair_dynamic_splitalpha_outermod, 0.3, 1, 0.01, 0.5);
            addTextboxNumberCmdController(ConfigEntry.cl_crosshair_dynamic_splitdist, 7, true);
            AddIntervalCmdController(ConfigEntry.cl_crosshair_outlinethickness, 0.1, 3, 0.1, 1);
            addTextboxNumberCmdController(ConfigEntry.cl_crosshair_sniper_width, 1, false);
            AddIntervalCmdController(ConfigEntry.cl_crosshairalpha, 0, 255, 1, 200);
            AddComboboxCmdController(ConfigEntry.cl_crosshairdot, toggleStrings, 1, true);
            AddComboboxCmdController(ConfigEntry.cl_crosshairgap, toggleStrings, 1, true);
            AddComboboxCmdController(ConfigEntry.cl_crosshair_t, toggleStrings, 1, true);
            AddComboboxCmdController(ConfigEntry.cl_crosshairgap_useweaponvalue, toggleStrings, 1, true);
            addTextboxNumberCmdController(ConfigEntry.cl_crosshairsize, 5, false);
            AddIntervalCmdController(ConfigEntry.cl_crosshairstyle, 0, 5, 1, 2);
            addTextboxNumberCmdController(ConfigEntry.cl_crosshairthickness, 0.5, false);
            AddComboboxCmdController(ConfigEntry.cl_crosshairusealpha, toggleStrings, 1, true);
            addTextboxNumberCmdController(ConfigEntry.cl_fixedcrosshairgap, 3, false);

            // текстовые аргументы
            addGroupHeader(Res.CategoryOther);
            addTextboxStringCmdController(ConfigEntry.say, "vk.com/exideprod");
            addTextboxStringCmdController(ConfigEntry.say_team, "Hello world!");
            addTextboxStringCmdController(ConfigEntry.connect, "12.34.56.78:27015");

            addGroupHeader(Res.CategoryNetGraphSettings);
            AddComboboxCmdController(ConfigEntry.net_graph, toggleStrings, 0, true);
            addTextboxNumberCmdController(ConfigEntry.net_graphheight, 64, true);
            addTextboxNumberCmdController(ConfigEntry.net_graphpos, 1, true);
            AddComboboxCmdController(ConfigEntry.net_graphproportionalfont, toggleStrings, 0, true);
        }

        void InitExtra()
        {
            // custom command execution
            this.customCmdHeaderCheckbox.Click += HandleEntryClick;
            this.customCmdHeaderCheckbox.Tag = ConfigEntry.ExecCustomCmds;
            this.entryUiBindings.Add(ConfigEntry.ExecCustomCmds, new EntryUiBinding()
            {
                AttachedCheckbox = customCmdHeaderCheckbox,
                Focus = () => extraTabButton.IsChecked = true,
                HandleState = (state) => customCmdHeaderCheckbox.IsEnabled = state != EntryStateBinding.InvalidState,
                Restore = () =>
                {
                    this.ClearPanel_s(customCmdPanel);

                    cmdTextbox.Text = string.Empty;
                    customCmdHeaderCheckbox.IsChecked = false;
                },
                Generate = () =>
                {
                    string aliasName = $"{GeneratePrefix()}_exec";
                    string[] cmds = customCmdPanel.Children.OfType<ButtonBase>()
                        .Select(b => b.Content.ToString()).ToArray();

                    AliasCmd execCmdsAlias = new AliasCmd(aliasName, cmds.Select(cmd => new SingleCmd(cmd)));

                    return new ParametrizedEntry<string[]>()
                    {
                        PrimaryKey = ConfigEntry.ExecCustomCmds,
                        Cmd = new SingleCmd(aliasName),
                        IsMetaScript = false,
                        Type = EntryType.Dynamic,
                        Dependencies = new CommandCollection(execCmdsAlias),
                        Arg = cmds
                    };
                },
                UpdateUI = (entry) =>
                {
                    customCmdHeaderCheckbox.IsChecked = true;

                    IParametrizedEntry<string[]> extendedEntry = (IParametrizedEntry<string[]>)entry;
                    string[] cmds = extendedEntry.Arg;

                    this.ClearPanel_s(customCmdPanel);

                    foreach (string cmd in cmds)
                    {
                        cmdTextbox.Text = cmd;
                        addCmdButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                }
            });

            // cycle crosshairs
            this.cycleChHeaderCheckbox.Click += HandleEntryClick;
            this.cycleChHeaderCheckbox.Tag = ConfigEntry.CycleCrosshair;
            this.entryUiBindings.Add(ConfigEntry.CycleCrosshair, new EntryUiBinding()
            {
                AttachedCheckbox = cycleChHeaderCheckbox,
                Focus = () =>
                {
                    extraTabButton.IsChecked = true;
                    cycleChHeaderCheckbox.Focus();
                },
                Generate = () =>
                {
                    int crosshairCount = (int)cycleChSlider.Value;
                    string prefix = GeneratePrefix();
                    string scriptName = $"{prefix}_crosshairLoop";

                    // Зададим имена итерациям
                    string[] iterationNames = new string[crosshairCount];

                    for (int i = 0; i < crosshairCount; i++)
                        iterationNames[i] = $"{scriptName}{i + 1}";

                    List<CommandCollection> iterations = new List<CommandCollection>();

                    for (int i = 0; i < crosshairCount; i++)
                    {
                        CommandCollection currentIteration = new CommandCollection()
                        {
                            new SingleCmd($"exec {prefix}_ch{i + 1}"),
                            new SingleCmd($"echo \"Crosshair {i + 1} loaded\"")
                        };
                        iterations.Add(currentIteration);
                    }

                    CycleCmd crosshairLoop = new CycleCmd(scriptName, iterations, iterationNames);

                    // Задаем начальную команду для алиаса
                    CommandCollection dependencies = new CommandCollection();

                    // И добавим в конец все итерации нашего цикла
                    foreach (Executable iteration in crosshairLoop)
                        dependencies.Add(iteration);

                    return new ParametrizedEntry<int>()
                    {
                        PrimaryKey = ConfigEntry.CycleCrosshair,
                        Cmd = new SingleCmd(scriptName),
                        Type = EntryType.Dynamic,
                        IsMetaScript = false,
                        Arg = crosshairCount,
                        Dependencies = dependencies
                    };
                },
                UpdateUI = (entry) =>
                {
                    cycleChHeaderCheckbox.IsChecked = true;
                    int crosshairCount = (entry as IParametrizedEntry<int>).Arg;

                    cycleChSlider.Value = crosshairCount;
                },
                Restore = () =>
                {
                    cycleChHeaderCheckbox.IsChecked = false;
                    cycleChSlider.Value = cycleChSlider.Minimum;
                },
                HandleState = (state) => cycleChHeaderCheckbox.IsEnabled =
                    state != EntryStateBinding.Default && state != EntryStateBinding.InvalidState
            });

            cycleChSlider.ValueChanged += (_, __) =>
            {
                if ((bool)cycleChHeaderCheckbox.IsChecked == true)
                    this.AddEntry(ConfigEntry.CycleCrosshair);
            };

            // volume regulator
            this.volumeRegulatorCheckbox.Click += HandleEntryClick;
            this.volumeRegulatorCheckbox.Tag = ConfigEntry.VolumeRegulator;
            this.entryUiBindings.Add(ConfigEntry.VolumeRegulator, new EntryUiBinding()
            {
                AttachedCheckbox = volumeRegulatorCheckbox,
                Focus = () =>
                {
                    extraTabButton.IsChecked = true;
                    volumeRegulatorCheckbox.Focus();
                },
                Generate = () =>
                {
                    double minVolume = Math.Round(minVolumeSlider.Value, 2);
                    double maxVolume = Math.Round(maxVolumeSlider.Value, 2);
                    double volumeStep = Math.Round(volumeStepSlider.Value, 2);
                    volumeStep = volumeStep == 0 ? 0.01 : volumeStep;
                    bool volumeUp = volumeDirectionCombobox.SelectedIndex == 1;

                    // Определяем промежуточные значения от максимума к минимуму
                    List<double> volumeValues = new List<double>();

                    double currentValue = maxVolume;

                    while (currentValue >= minVolume)
                    {
                        volumeValues.Add(currentValue);
                        currentValue -= volumeStep;
                        string formatted = Executable.FormatNumber(currentValue, false);
                        Executable.TryParseDouble(formatted, out currentValue);
                    }
                    // Если минимальное значение не захватилось, то добавим его вручную
                    if (volumeValues.Last() != minVolume)
                        volumeValues.Add(minVolume);

                    // Теперь упорядочим по возрастанию
                    volumeValues.Reverse();

                    // Создаем цикл
                    string volumeUpCmd = "volume_up";
                    string volumeDownCmd = "volume_down";

                    SingleCmd[] iterationNames = volumeValues
                        .Select(v => new SingleCmd($"volume_{Executable.FormatNumber(v, false)}")).ToArray();

                    CommandCollection dependencies = new CommandCollection();

                    for (int i = 0; i < volumeValues.Count; i++)
                    {
                        double value = volumeValues[i];
                        string formattedValue = Executable.FormatNumber(value, false);

                        CommandCollection iterationCmds = new CommandCollection();

                        // Задаем звук на текущей итерации с комментарием в консоль
                        SingleCmd volumeCmd = new SingleCmd($"volume {formattedValue}");
                        iterationCmds.Add(volumeCmd);
                        iterationCmds.Add(new SingleCmd($"echo {volumeCmd.ToString()}"));

                        if (i == 0)
                        {
                            iterationCmds.Add(
                                new AliasCmd(volumeDownCmd, new SingleCmd("echo Volume: Min")));
                            iterationCmds.Add(
                                new AliasCmd(volumeUpCmd, iterationNames[i + 1]));
                        }
                        else if (i == volumeValues.Count - 1)
                        {
                            iterationCmds.Add(
                                new AliasCmd(volumeUpCmd, new SingleCmd("echo Volume: Max")));
                            iterationCmds.Add(
                                new AliasCmd(volumeDownCmd, iterationNames[i - 1]));
                        }
                        else
                        {
                            iterationCmds.Add(
                                new AliasCmd(volumeDownCmd, iterationNames[i - 1]));
                            iterationCmds.Add(
                                new AliasCmd(volumeUpCmd, iterationNames[i + 1]));
                        }

                        // Добавим зависимость
                        dependencies.Add(new AliasCmd(iterationNames[i].ToString(), iterationCmds));
                    }

                    // По умолчанию будет задано минимальное значение звука
                    dependencies.Add(iterationNames[0]);

                    return new ParametrizedEntry<double[]>()
                    {
                        PrimaryKey = ConfigEntry.VolumeRegulator,
                        Cmd = volumeUp ? new SingleCmd(volumeUpCmd) : new SingleCmd(volumeDownCmd),
                        Type = EntryType.Semistatic,
                        IsMetaScript = false,
                        Dependencies = dependencies,
                        Arg = new double[] { minVolume, maxVolume, volumeStep }
                    };
                },
                UpdateUI = (entry) =>
                {
                    volumeRegulatorCheckbox.IsChecked = true;
                    double[] args = ((IParametrizedEntry<double[]>)entry).Arg;

                    minVolumeSlider.Value = args[0];
                    maxVolumeSlider.Value = args[1];
                    volumeStepSlider.Value = args[2];
                },
                Restore = () =>
                {
                    volumeRegulatorCheckbox.IsChecked = false;
                },
                HandleState = (state) => volumeRegulatorCheckbox.IsEnabled =
                    state != EntryStateBinding.InvalidState && state != EntryStateBinding.Default
            });
        }

        void InitAliasController()
        {
            this.entryUiBindings.Add(ConfigEntry.ExtraAliasSet, new EntryUiBinding()
            {
                Generate = () =>
                {
                    List<ParametrizedEntry<Entry[]>> aliases =
                    new List<ParametrizedEntry<Entry[]>>();

                    CommandCollection dependencies = new CommandCollection();

                    foreach (ContentControl aliaselement in aliasPanel.Children.OfType<ContentControl>())
                    {
                        string aliasName = aliaselement.Content.ToString();
                        List<Entry> attachedEntries = (List<Entry>)aliaselement.Tag;

                        // Выпишем все зависимости, которые есть для текущего элемента
                        foreach (Entry entry in attachedEntries)
                            foreach (Executable dependency in entry.Dependencies)
                                dependencies.Add(dependency);

                        ParametrizedEntry<Entry[]> aliasEntry = new ParametrizedEntry<Entry[]>()
                        {
                            PrimaryKey = ConfigEntry.ExtraAlias,
                            Cmd = new SingleCmd(aliasName),
                            IsMetaScript = false,
                            Type = EntryType.Dynamic,
                            Arg = attachedEntries.ToArray()
                        };

                        AliasCmd alias = new AliasCmd(
                            aliaselement.Content.ToString(),
                            attachedEntries.Select(e => e.Cmd));

                        aliases.Add(aliasEntry);
                        dependencies.Add(alias);
                    }

                    // сформируем итоговый элемент конфига
                    return new ParametrizedEntry<Entry[]>()
                    {
                        PrimaryKey = ConfigEntry.ExtraAliasSet,
                        Cmd = null,
                        IsMetaScript = false,
                        Type = EntryType.Dynamic,
                        Arg = aliases.ToArray(),
                        Dependencies = dependencies
                    };
                },
                UpdateUI = (entry) =>
                {
                    ParametrizedEntry<Entry[]> extendedEntry = (ParametrizedEntry<Entry[]>)entry;

                    Entry[] aliases = extendedEntry.Arg;

                    foreach (Entry alias in aliases)
                    {
                        AddAliasButton(
                            alias.Cmd.ToString(),
                            (alias as ParametrizedEntry<Entry[]>).Arg.ToList());
                    }
                },
                Restore = () =>
                {
                    this.ResetAttachmentPanels();
                    this.ClearPanel_s(aliasPanel);
                }
            });
        }
        #endregion

        #region Framework
        void ColorizeKeyboard()
        {
            // сбрасываем цвета перед обновлением
            foreach (Button key in this.kb)
            {
                key.ClearValue(ButtonBase.BackgroundProperty);
                key.ClearValue(ButtonBase.ForegroundProperty);
            }

            SolidColorBrush keyInSequenceBackground = (SolidColorBrush)this.FindResource("SecondaryAccentBrush");
            SolidColorBrush keyInSequenceForeground = (SolidColorBrush)this.FindResource("SecondaryAccentForegroundBrush");
            
            SolidColorBrush firstKeyBackground = (SolidColorBrush)this.FindResource("PrimaryHueMidBrush");
            SolidColorBrush firstKeyForeground = (SolidColorBrush)this.FindResource("PrimaryHueMidForegroundBrush");

            SolidColorBrush secondKeyBackground = (SolidColorBrush)this.FindResource("PrimaryHueDarkBrush");
            SolidColorBrush secondKeyForeground = (SolidColorBrush)this.FindResource("PrimaryHueDarkForegroundBrush");

            // Все элементы конфига
            var allEntries = this.cfgManager.Entries;

            // Закрасим первым цветом все кнопки, которые 1-е в последовательности
            allEntries.ToList()
            .ForEach(pair =>
            {
                Button button = this.kb.GetButtonByName(pair.Key.Keys[0]);
                button.Background = firstKeyBackground;
                button.Foreground = firstKeyForeground;
            });

            // Если в текущей последовательности 1 кнопка - закрасим вторым цветом все кнопки, 
            // которые связаны с текущей и являются вторыми в последовательности
            if (currentKeySequence != null && this.currentKeySequence.Keys.Length == 1)
            {
                allEntries.Where(p => p.Key.Keys.Length == 2 && p.Key.Keys[0] == currentKeySequence[0]).ToList()
               .ForEach(pair =>
               {
                   Button button = this.kb.GetButtonByName(pair.Key.Keys[1]);
                   button.Background = secondKeyBackground;
                   button.Foreground = secondKeyForeground;
               });
            }

            // Теперь выделим акцентным цветом все кнопки в текущей последовательности
            if (currentKeySequence != null)
            {
                Button seqKey1 = this.kb.GetButtonByName(currentKeySequence[0]);
                seqKey1.Background = keyInSequenceBackground;
                seqKey1.Foreground = keyInSequenceForeground;
                if (currentKeySequence.Keys.Length == 2)
                {
                    Button seqKey2 = this.kb.GetButtonByName(currentKeySequence[1]);
                    seqKey2.Background = keyInSequenceBackground;
                    seqKey2.Foreground = keyInSequenceForeground;
                }
            }
        }

        string Localize(ConfigEntry cfgEntry)
        {
            string result = Res.ResourceManager.GetString(cfgEntry.ToString());
            return result ?? cfgEntry.ToString();
            //return result ?? throw new Exception($"Resource key {cfgEntry.ToString()} not found");
        }

        string GeneratePrefix()
        {
            System.Text.StringBuilder prefixBuilder = new System.Text.StringBuilder();
            
            switch (this.StateBinding)
            {
                case EntryStateBinding.KeyDown:
                    {
                        prefixBuilder.Append($"{this.currentKeySequence.ToString()}_");
                        prefixBuilder.Append("down");
                        break;
                    }
                case EntryStateBinding.KeyUp:
                    {
                        prefixBuilder.Append($"{this.currentKeySequence.ToString()}_");
                        prefixBuilder.Append("up");
                        break;
                    }
                case EntryStateBinding.Default:
                    {
                        prefixBuilder.Append("default");
                        break;
                    }
                case EntryStateBinding.Alias:
                    {
                        string aliasName = (aliasPanel.Tag as ContentControl).Content.ToString();
                        prefixBuilder.Append($"{aliasName}");
                        break;
                    }
                default:
                    throw new Exception($"Попытка генерации префикса при состоянии {this.StateBinding}");
            }

            return prefixBuilder.ToString();
        }

        /// <summary>
        /// Обработчик нажатия чекбоксов для добавления/удаления элементов конфига
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        void HandleEntryClick(object sender, RoutedEventArgs args)
        {
            CheckBox checkbox = (CheckBox)sender;
            ConfigEntry cfgEntry = (ConfigEntry)checkbox.Tag;

            // Получим обработчика и 
            EntryUiBinding entryBinding = this.entryUiBindings[cfgEntry];
            Entry entry = (Entry)entryBinding.Generate();

            if ((bool)checkbox.IsChecked)
                this.AddEntry(entry);
            else
                this.RemoveEntry(entry);

            // Обновим панели
            this.UpdateAttachmentPanels();
        }
        
        BindEntry ConvertToBindEntry(Entry entry)
        {
            BindEntry bindEntry = (BindEntry)entry;

            Executable onKeyDown, onKeyUp;

            if (entry.IsMetaScript)
            {
                onKeyDown = new SingleCmd($"+{entry.Cmd}");
                onKeyUp = new SingleCmd($"-{entry.Cmd}");
            }
            else
            {
                bool isKeyDownBinding = this.StateBinding == EntryStateBinding.KeyDown;

                onKeyDown = isKeyDownBinding ? entry.Cmd : null;
                onKeyUp = isKeyDownBinding ? null : entry.Cmd;
            }

            bindEntry.OnKeyDown = onKeyDown;
            bindEntry.OnKeyRelease = onKeyUp;

            return bindEntry;
        }

        void AddEntry(ConfigEntry cfgEntry)
        {
            Entry generatedEntry = (Entry)this.entryUiBindings[cfgEntry].Generate();
            this.AddEntry(generatedEntry);
        }

        void RemoveEntry(ConfigEntry cfgEntry)
        {
            Entry generatedEntry = (Entry)this.entryUiBindings[cfgEntry].Generate();
            this.RemoveEntry(generatedEntry);
        }

        void AddEntry(Entry entry)
        {
            if (this.StateBinding == EntryStateBinding.KeyDown || this.StateBinding == EntryStateBinding.KeyUp)
            {
                BindEntry bindEntry = this.ConvertToBindEntry(entry);
                this.cfgManager.AddEntry(this.currentKeySequence, bindEntry);
            }
            else if (this.StateBinding == EntryStateBinding.Default)
            {
                this.cfgManager.AddEntry(entry);
            }
            else if (this.StateBinding == EntryStateBinding.Alias)
            {
                // Добавляем текущий элемент к коллекции, привязанной к выбранной кнопке
                FrameworkElement targetElement = aliasPanel.Tag as FrameworkElement;
                List<Entry> attachedToAlias = targetElement.Tag as List<Entry>;
                targetElement.Tag = attachedToAlias
                    .Where(attachedEntry => attachedEntry.PrimaryKey != entry.PrimaryKey)
                    .Concat(new Entry[] { entry }).ToList();

                // И вызываем обработчика пользовательских алиасов
                Entry aliasSetEntry = (Entry)this.entryUiBindings[ConfigEntry.ExtraAliasSet].Generate();
                this.cfgManager.AddEntry(aliasSetEntry);
            }
            else
                throw new Exception($"Состояние {this.StateBinding} при добавлении элемента");
        }

        void RemoveEntry(Entry entry)
        {
            if (this.StateBinding == EntryStateBinding.KeyDown || this.StateBinding == EntryStateBinding.KeyUp)
            {
                BindEntry bindEntry = this.ConvertToBindEntry(entry);
                this.cfgManager.RemoveEntry(this.currentKeySequence, bindEntry);
            }
            else if (this.StateBinding == EntryStateBinding.Default)
            {
                this.cfgManager.RemoveEntry(entry);
            }
            else if (this.StateBinding == EntryStateBinding.Alias)
            {                
                if (entry.PrimaryKey != ConfigEntry.ExtraAliasSet)
                {
                    // Добавляем текущий элемент к коллекции, привязанной к выбранной кнопке
                    FrameworkElement targetElement = aliasPanel.Tag as FrameworkElement;
                    List<Entry> attachedToAlias = targetElement.Tag as List<Entry>;
                    targetElement.Tag = attachedToAlias
                        .Where(attachedEntry => attachedEntry.PrimaryKey != entry.PrimaryKey)
                        .ToList();

                    // Напрямую обновим узел в менеджере
                    Entry aliasSetEntry = (Entry) this.entryUiBindings[ConfigEntry.ExtraAliasSet].Generate();
                    this.cfgManager.AddEntry(aliasSetEntry);
                }
                else
                {
                    // Если удаляем основной узел со всеми алиасами, то напрямую стираем его из менеджера
                    this.cfgManager.RemoveEntry(entry);
                }
            }
            else
                throw new Exception($"Состояние {this.StateBinding} при попытке удалить элемент");
        }

        void ResetAttachmentPanels()
        {
            // Получим предыдущие элементы и сбросим связанные с ними элементы интерфейса
            // Для этого объединим коллекции элементов из всех панелей
            List<WrapPanel> attachmentPanels = new List<WrapPanel>()
            {
                onKeyDownAttachmentsPanel,
                onKeyReleaseAttachmentsPanel,
                solidAttachmentsPanel
            };

            IEnumerable<FrameworkElement> mergedElements = attachmentPanels.SelectMany(p => p.Children.Cast<FrameworkElement>());

            foreach (FrameworkElement element in mergedElements)
            {
                EntryUiBinding entryBinding = this.entryUiBindings[(ConfigEntry)element.Tag];
                // Метод, отвечающий непосредственно за сброс состояния интерфейса
                entryBinding.Restore();
            }

            // Очистим панели
            attachmentPanels.ForEach(p => p.Children.Clear());
        }

        /// <summary>
        /// Метод для обновления панелей с привязанными к сочетанию клавиш элементами конфига
        /// </summary>
        void UpdateAttachmentPanels()
        {
            // Очистим панели и сбросим настройки интерфейса
            ResetAttachmentPanels();

            void AddAttachment(ConfigEntry cfgEntry, WrapPanel panel)
            {
                // Создадим новую кнопку и зададим нужный стиль

                ButtonBase chip = new Chip
                {
                    Content = Localize(cfgEntry) //CurrentLocale[cfgEntry]
                };
                chip.Click += HandleAttachedEntryClick;
                chip.Style = (Style)this.Resources["BubbleButton"];
                chip.Tag = cfgEntry;
                chip.FontSize = 13;
                chip.Height = 17;
                
                // Добавим в нужную панель
                panel.Children.Add(chip);
            };

            // Согласно текущему состоянию выберем элементы
            if (this.StateBinding == EntryStateBinding.KeyDown || this.StateBinding == EntryStateBinding.KeyUp)
            {
                if (!this.cfgManager.Entries.TryGetValue(this.currentKeySequence, out List<BindEntry> attachedEntries)) return;

                /*
                 * Мета-скрипты привязываются только к нажатию кнопок (хотя отжатие обработано тоже)
                 * В других случаях может быть обработано либо нажатие, либо отжатие кнопки
                 */

                // Теперь заполним панели новыми элементами
                attachedEntries.ForEach(entry =>
                {
                    // Добавим в нужную панель
                    if (entry.OnKeyDown != null)
                        AddAttachment(entry.PrimaryKey, onKeyDownAttachmentsPanel);
                    else
                        AddAttachment(entry.PrimaryKey, onKeyReleaseAttachmentsPanel);

                    // Обновим интерфейс согласно элементам, привязанным к текущему состоянию
                    attachedEntries.Where(e => this.IsEntryAttachedToCurrentState(e))
                        .ToList().ForEach(e => this.entryUiBindings[e.PrimaryKey].UpdateUI(e));
                });
            }
            else if (this.StateBinding == EntryStateBinding.Default)
            {
                // Получаем все элементы по умолчанию, которые должны быть отображены в панели
                List<Entry> attachedEntries = this.cfgManager.DefaultEntries
                    .Where(e => this.entryUiBindings[e.PrimaryKey].AttachedCheckbox != null).ToList();

                // Теперь заполним панели новыми элементами
                attachedEntries.ForEach(entry =>
                {
                    AddAttachment(entry.PrimaryKey, solidAttachmentsPanel);
                    // Обновим интерфейс согласно элементам, привязанным к текущему состоянию
                    this.entryUiBindings[entry.PrimaryKey].UpdateUI(entry);
                });
            }
            else if (this.StateBinding == EntryStateBinding.Alias)
            {
                if (this.aliasPanel.Tag == null) return;

                // Узнаем какие элементы привязаны к текущей команде
                List<Entry> attachedEntries = (List<Entry>)(this.aliasPanel.Tag as FrameworkElement).Tag;

                attachedEntries.ForEach(entry =>
                {
                    AddAttachment(entry.PrimaryKey, solidAttachmentsPanel);
                    // Обновим интерфейс согласно элементам, привязанным к текущему состоянию
                    this.entryUiBindings[entry.PrimaryKey].UpdateUI(entry);
                });
            }
            else { } // InvalidState
        }
        
        /// <summary>
        /// Обработчик нажатия на привязанный элемент из конфига
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="args"></param>
        void HandleAttachedEntryClick(object obj, RoutedEventArgs args)
        {
            // Узнаем за привязку к какому типу отвечает нажатая кнопка
            FrameworkElement element = (FrameworkElement)obj;
            if (element.Tag == null) return; // TODO: УДАЛИТЬ
            ConfigEntry entry = (ConfigEntry) element.Tag;

            // Получим обработчика и переведем фокус на нужный элемент
            this.entryUiBindings[entry].Focus();
        }

        private void GenerateConfig(object sender, RoutedEventArgs e)
        {
            if (cfgNameBox.Text.Trim().Length == 0)
                cfgNameBox.Text = "ConfigMakerCfg";

            string name = $"{cfgNameBox.Text}.cfg";

            this.cfgManager.GenerateCfg(name);
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{name}\"");
        }

        bool IsEntryAttachedToCurrentState(BindEntry entry)
        {
            if (this.StateBinding == EntryStateBinding.Default
                || this.StateBinding == EntryStateBinding.Alias
                || this.StateBinding == EntryStateBinding.InvalidState)
                throw new Exception(); // TODO: УБРАТЬ

            // Если сбрасываем привязанные к нажатию элементы, и у элемента определено действие на нажатие, то подходит
            if (this.StateBinding == EntryStateBinding.KeyDown && entry.OnKeyDown != null)
                return true;
            // Если привязанные к отжатию и у нас обрабатывается только отжатие, то подходит
            else if (this.StateBinding == EntryStateBinding.KeyUp && entry.OnKeyRelease != null && entry.OnKeyDown == null)
                return true;
            else
                return false;
        }

        void AddAliasButton(string name, List<Entry> attachedEntries)
        {
            if (aliasPanel.Children.OfType<ButtonBase>().Any(b => b.Content.ToString() == name))
                return;

            Chip chip = new Chip
            {
                Content = name,
                Tag = attachedEntries,
                Style = (Style)this.Resources["BubbleButton"]
            };

            chip.Click += (_, __) =>
            {
                // При нажатии на кнопку задаем в теге 
                // панели какая выбрана в данный момент
                aliasPanel.Tag = chip;

                // Очистим панели и заполним их согласно выбранному алиасу
                UpdateAttachmentPanels();
            };

            aliasPanel.Children.Add(chip);
            aliasPanel.Tag = chip;

            if (this.StateBinding == EntryStateBinding.InvalidState)
                this.StateBinding = EntryStateBinding.Alias;

            // Программно настроим привязку 
            Binding binding = new Binding("Tag")
            {
                Source = aliasPanel,
                Converter = new TagToFontWeightConverter(),
                ConverterParameter = chip
            };
            chip.SetBinding(ButtonBase.FontWeightProperty, binding);

            // Искусственно генерируем клик на новую кнопку, чтобы перестроить интерфейс
            chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        void ClearPanel_s(WrapPanel panel)
        {
            foreach (FrameworkElement element in panel.Children)
                BindingOperations.ClearAllBindings(element);

            panel.Children.Clear();
            panel.Tag = null;
        }

        void UpdateCfgManager()
        {
            // Сбросим все настройки от прошлого конфига
            foreach (EntryUiBinding binding in this.entryUiBindings.Values)
                binding.Restore();

            // Зададим привязку к дефолтному состоянию
            this.StateBinding = EntryStateBinding.Default;

            foreach (Entry entry in this.cfgManager.DefaultEntries)
                this.entryUiBindings[entry.PrimaryKey].UpdateUI(entry);

            this.UpdateAttachmentPanels();
            this.ColorizeKeyboard();
        }

        string GetTargetFolder()
        {
            string cfgPath = cfgFolderPath.Text.Trim();

            if (cfgPath.Length > 0 && Directory.Exists(cfgPath))
            {
                return cfgPath;
            }
            else
            {
                return Directory.GetCurrentDirectory();
            }
        }

        void HandleException(string userMsg, Exception ex)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine(userMsg);

            Exception currentException = ex;

            do
            {
                builder.AppendLine($"{currentException.Message}");
                currentException = currentException.InnerException;
            } while (currentException != null);

            builder.Append($"StackTrace: {ex.StackTrace}");
            MessageBox.Show(builder.ToString());
        }
        #endregion
    }
}
