using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WpfControls = System.Windows.Controls;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace BannerManager
{
    public partial class MainWindow : Window
    {
        private readonly PresetStore _store = new();
        private List<AlertPreset> _presets = new();
        private AlertPreset? _selected;

        public MainWindow()
        {
            InitializeComponent();
            AlertPathBox.Text = AlertFileService.DefaultAlertFilePath;

            _presets = _store.Load();
            RefreshList();

            if (_presets.Count > 0)
            {
                PresetList.SelectedIndex = 0;
            }
            else
            {
                CreateNewPreset();
            }
        }

        private void RefreshList()
        {
            PresetList.ItemsSource = null;
            PresetList.ItemsSource = _presets.OrderBy(p => p.Name).ToList();
        }

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetList.SelectedItem is AlertPreset p)
            {
                _selected = p;
                LoadEditor(p);
            }
        }

        private void LoadEditor(AlertPreset p)
        {
            NameBox.Text = p.Name;
            SummaryBox.Text = p.Summary;
            MessageTextBox.Text = p.Message;
            BgBox.Text = p.BackgroundColor;
            FgBox.Text = p.ForegroundColor;
            ContactNameBox.Text = p.AlertContactName;
            ContactPhoneBox.Text = p.AlertContactPhone;
            ContactEmailBox.Text = p.AlertContactEmail;
            ContactTeamsBox.Text = p.AlertContactTeams;
            SiteBox.Text = p.Site;

            SetCombo(LevelBox, p.Level);
            UpdatePreviews();
        }

        private void UpdateFromEditor(AlertPreset p)
        {
            p.Name = NameBox.Text.Trim();
            p.Level = GetCombo(LevelBox);
            p.Summary = SummaryBox.Text.Trim();
            p.Message = MessageTextBox.Text.Trim();
            p.BackgroundColor = BgBox.Text.Trim();
            p.ForegroundColor = FgBox.Text.Trim();
            p.AlertContactName = ContactNameBox.Text.Trim();
            p.AlertContactPhone = ContactPhoneBox.Text.Trim();
            p.AlertContactEmail = ContactEmailBox.Text.Trim();
            p.AlertContactTeams = ContactTeamsBox.Text.Trim();
            p.Site = SiteBox.Text.Trim();
        }

        private static string GetCombo(WpfControls.ComboBox box)
        {
            if (box.SelectedItem is WpfControls.ComboBoxItem item && item.Content is string s)
            {
                return s;
            }
            return "routine";
        }

        private static void SetCombo(WpfControls.ComboBox box, string value)
        {
            foreach (var it in box.Items.OfType<WpfControls.ComboBoxItem>())
            {
                if ((it.Content as string)?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
                {
                    box.SelectedItem = it;
                    return;
                }
            }
            box.SelectedIndex = 0;
        }

        private void UpdatePreviews()
        {
            BgPreview.Background = new SolidColorBrush(ParseHexColor(BgBox.Text, Colors.DarkRed));
            FgPreview.Background = new SolidColorBrush(ParseHexColor(FgBox.Text, Colors.White));
        }

        private static System.Windows.Media.Color ParseHexColor(string raw, System.Windows.Media.Color fallback)
        {
            try
            {
                var s = raw.Trim();
                if (s.StartsWith("#", StringComparison.Ordinal))
                {
                    s = s[1..];
                }
                if (s.Length != 6)
                {
                    return fallback;
                }
                var r = Convert.ToByte(s.Substring(0, 2), 16);
                var g = Convert.ToByte(s.Substring(2, 2), 16);
                var b = Convert.ToByte(s.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
            catch
            {
                return fallback;
            }
        }

        private void New_Click(object sender, RoutedEventArgs e) => CreateNewPreset();

        private void CreateNewPreset()
        {
            var p = new AlertPreset
            {
                Name = "New Alert",
                Level = "routine",
                BackgroundColor = "#B00020",
                ForegroundColor = "#FFFFFF"
            };
            _presets.Add(p);
            _store.Save(_presets);
            RefreshList();
            PresetList.SelectedItem = _presets.FirstOrDefault(x => x.Id == p.Id);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                return;
            }

            UpdateFromEditor(_selected);

            if (string.IsNullOrWhiteSpace(_selected.Name))
            {
                MessageBox.Show("Preset Name is required.", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _store.Save(_presets);
            RefreshList();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                return;
            }

            if (MessageBox.Show($"Delete '{_selected.Name}'?", "BannerManager", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _presets.RemoveAll(p => p.Id == _selected.Id);
            _selected = null;
            _store.Save(_presets);
            RefreshList();

            if (_presets.Count > 0)
            {
                PresetList.SelectedIndex = 0;
            }
            else
            {
                CreateNewPreset();
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Import alert JSON"
            };

            if (dlg.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var p = AlertFileService.ImportFromJson(dlg.FileName);
                p.Name = string.IsNullOrWhiteSpace(p.Name) ? "Imported Alert" : p.Name;
                _presets.Add(p);
                _store.Save(_presets);
                RefreshList();
                PresetList.SelectedItem = _presets.FirstOrDefault(x => x.Id == p.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BannerManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSamples_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var repoRoot = AppDomain.CurrentDomain.BaseDirectory;
                // Running from bin/.../ - move up a bit and try to locate samples/alerts relative to repo.
                var samplesCandidates = new[]
                {
                    Path.Combine(repoRoot, "samples", "alerts"),
                    Path.Combine(repoRoot, "..", "..", "..", "..", "samples", "alerts"),
                    Path.Combine(repoRoot, "..", "..", "..", "..", "..", "samples", "alerts")
                }.Select(Path.GetFullPath).Distinct().ToList();

                var samplesDir = samplesCandidates.FirstOrDefault(Directory.Exists);
                if (samplesDir == null)
                {
                    MessageBox.Show("Could not locate samples/alerts folder.", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var files = Directory.GetFiles(samplesDir, "*.json").OrderBy(f => f).ToList();
                var imported = 0;
                foreach (var file in files)
                {
                    try
                    {
                        var p = AlertFileService.ImportFromJson(file);
                        p.Name = $"Sample: {Path.GetFileNameWithoutExtension(file)}";
                        _presets.Add(p);
                        imported++;
                    }
                    catch
                    {
                        // skip invalid sample
                    }
                }

                _store.Save(_presets);
                RefreshList();
                MessageBox.Show($"Imported {imported} sample alert(s).", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BannerManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PickBg_Click(object sender, RoutedEventArgs e) => PickColor(BgBox);
        private void PickFg_Click(object sender, RoutedEventArgs e) => PickColor(FgBox);

        private void PickColor(WpfControls.TextBox target)
        {
            var cd = new WinForms.ColorDialog
            {
                FullOpen = true
            };

            var result = cd.ShowDialog();
            if (result == WinForms.DialogResult.OK)
            {
                var c = cd.Color;
                target.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                UpdatePreviews();
            }
        }

        private void BrowseAlertFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select alert file location",
                FileName = "alert.json"
            };

            if (dlg.ShowDialog() == true)
            {
                AlertPathBox.Text = dlg.FileName;
            }
        }

        private void Trigger_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                MessageBox.Show("Select an alert preset first.", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateFromEditor(_selected);
            UpdatePreviews();

            if (string.IsNullOrWhiteSpace(_selected.Summary) || string.IsNullOrWhiteSpace(_selected.Message))
            {
                MessageBox.Show("Summary and Message are required.", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var path = AlertPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = AlertFileService.DefaultAlertFilePath;
                AlertPathBox.Text = path;
            }

            try
            {
                AlertFileService.WriteAlert(path, _selected);
                MessageBox.Show($"Triggered alert by writing:\n{path}", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BannerManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var path = AlertPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = AlertFileService.DefaultAlertFilePath;
                AlertPathBox.Text = path;
            }

            try
            {
                AlertFileService.ClearAlert(path);
                MessageBox.Show($"Cleared alert file:\n{path}", "BannerManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BannerManager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}


