using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace TikTokMusicPlayer
{
    public partial class SettingsWindow : Window
    {
        private AppSettings settings;
        public event EventHandler<AppSettings>? SettingsChanged;

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            settings = currentSettings;
            LoadSettings();
        }

        private void LoadSettings()
        {
            ChkAutoRecord.IsChecked = settings.AutoRecordOnPlay;
            TxtOutputPath.Text = settings.RecordingOutputPath;
            TxtRecordingFileName.Text = settings.RecordingFileName;
            TxtWidth.Text = settings.RecordingWidth.ToString();
            TxtHeight.Text = settings.RecordingHeight.ToString();
            TxtLyricOffset.Text = settings.LyricOffset.ToString("F1");
        }

        private void SaveSettings()
        {
            settings.AutoRecordOnPlay = ChkAutoRecord.IsChecked ?? false;
            settings.RecordingOutputPath = TxtOutputPath.Text;
            settings.RecordingFileName = TxtRecordingFileName.Text;

            if (int.TryParse(TxtWidth.Text, out int width) && width > 0)
                settings.RecordingWidth = width;

            if (int.TryParse(TxtHeight.Text, out int height) && height > 0)
                settings.RecordingHeight = height;

            if (double.TryParse(TxtLyricOffset.Text, out double offset))
                settings.LyricOffset = offset;

            settings.Save();
            SettingsChanged?.Invoke(this, settings);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void ChkAutoRecord_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void TxtOutputPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void TxtRecordingFileName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void TxtResolution_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void TxtLyricOffset_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void BtnLyricOffsetDown_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtLyricOffset.Text, out double offset))
            {
                offset -= 0.5;
                TxtLyricOffset.Text = offset.ToString("F1");
            }
        }

        private void BtnLyricOffsetUp_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtLyricOffset.Text, out double offset))
            {
                offset += 0.5;
                TxtLyricOffset.Text = offset.ToString("F1");
            }
        }

        private void BtnLyricOffsetReset_Click(object sender, RoutedEventArgs e)
        {
            TxtLyricOffset.Text = "0.0";
        }

        private void BtnBrowseOutputPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择录制输出目录"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtOutputPath.Text = dialog.FolderName;
                SaveSettings();
            }
        }

        private void BtnPreset9x16_Click(object sender, RoutedEventArgs e)
        {
            TxtWidth.Text = "720";
            TxtHeight.Text = "1280";
            SaveSettings();
        }

        private void BtnPreset9x16HD_Click(object sender, RoutedEventArgs e)
        {
            TxtWidth.Text = "1080";
            TxtHeight.Text = "1920";
            SaveSettings();
        }

        private void BtnPreset9x16FullHD_Click(object sender, RoutedEventArgs e)
        {
            TxtWidth.Text = "1440";
            TxtHeight.Text = "2560";
            SaveSettings();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            SettingsChanged = null;
            base.OnClosed(e);
        }
    }
}
