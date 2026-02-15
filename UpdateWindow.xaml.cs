using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TikTokMusicPlayer
{
    public partial class UpdateWindow : Window
    {
        private UpdateInfo? updateInfo;
        private bool isDownloading = false;

        public UpdateWindow(UpdateInfo info)
        {
            InitializeComponent();
            updateInfo = info;
            
            CurrentVersionText.Text = info.CurrentVersion;
            LatestVersionText.Text = info.LatestVersion;
            ReleaseNotesText.Text = string.IsNullOrEmpty(info.ReleaseNotes) 
                ? "暂无更新说明" 
                : info.ReleaseNotes;
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (isDownloading || updateInfo == null)
                return;

            isDownloading = true;
            BtnUpdate.IsEnabled = false;
            BtnLater.IsEnabled = false;
            BtnSkip.IsEnabled = false;

            DownloadProgress.Visibility = Visibility.Visible;
            ProgressText.Visibility = Visibility.Visible;
            BtnUpdate.Content = "下载中...";

            try
            {
                var progress = new Progress<(long downloaded, long total, double speed)>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double percent = p.total > 0 ? (double)p.downloaded / p.total * 100 : 0;
                        DownloadProgress.Value = percent;
                        ProgressText.Text = $"{p.downloaded / 1024.0 / 1024.0:F1} MB / {p.total / 1024.0 / 1024.0:F1} MB ({p.speed:F0} KB/s)";
                    });
                });

                string? filePath = await UpdateManager.DownloadUpdateAsync(updateInfo.DownloadUrl, progress);

                if (string.IsNullOrEmpty(filePath))
                {
                    MessageBox.Show("下载更新失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetButtons();
                    return;
                }

                ProgressText.Text = "正在准备更新...";

                bool success = UpdateManager.ApplyUpdate(filePath, updateInfo.AssetName);

                if (success)
                {
                    this.Close();
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show("应用更新失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetButtons();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetButtons();
            }
        }

        private void ResetButtons()
        {
            isDownloading = false;
            BtnUpdate.IsEnabled = true;
            BtnLater.IsEnabled = true;
            BtnSkip.IsEnabled = true;
            BtnUpdate.Content = "立即更新";
            DownloadProgress.Visibility = Visibility.Collapsed;
            ProgressText.Visibility = Visibility.Collapsed;
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            SaveSkippedVersion();
            this.DialogResult = false;
            this.Close();
        }

        private void SaveSkippedVersion()
        {
            if (updateInfo == null)
                return;

            try
            {
                string skipFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TikTokMusicPlayer",
                    "skip_version.txt"
                );

                string? directory = Path.GetDirectoryName(skipFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(skipFile, updateInfo.LatestVersion);
            }
            catch { }
        }

        public static bool ShouldSkipVersion(string version)
        {
            try
            {
                string skipFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TikTokMusicPlayer",
                    "skip_version.txt"
                );

                if (File.Exists(skipFile))
                {
                    string skippedVersion = File.ReadAllText(skipFile).Trim();
                    return skippedVersion == version;
                }
            }
            catch { }

            return false;
        }
    }
}
