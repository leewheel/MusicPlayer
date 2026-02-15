using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TikTokMusicPlayer
{
    public partial class LyricSelectionWindow : Window
    {
        private List<LyricSearchResult> searchResults = new List<LyricSearchResult>();
        public LyricSearchResult? SelectedResult { get; private set; }
        public bool DownloadLyric { get; private set; } = true;
        public string? AudioFilePath { get; set; }

        public LyricSelectionWindow()
        {
            InitializeComponent();
        }

        public void SetResults(List<LyricSearchResult> results)
        {
            searchResults = results;
            LoadingText.Visibility = Visibility.Collapsed;
            
            if (results.Count == 0)
            {
                LoadingText.Text = "未找到歌词";
                LoadingText.Visibility = Visibility.Visible;
                return;
            }

            LyricListBox.ItemsSource = searchResults;
            if (searchResults.Count > 0)
            {
                LyricListBox.SelectedIndex = 0;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            SelectedResult = null;
            this.DialogResult = false;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedResult = null;
            this.DialogResult = false;
            this.Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (LyricListBox.SelectedItem is LyricSearchResult result)
            {
                SelectedResult = result;
                DownloadLyric = ChkDownloadLyric.IsChecked ?? false;
                
                if (DownloadLyric && !string.IsNullOrEmpty(AudioFilePath) && result.Lyric != null)
                {
                    SaveLyricToFile(AudioFilePath, result.Lyric.RawLrc);
                }
                
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("请选择一个歌词", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveLyricToFile(string audioFilePath, string lrcContent)
        {
            try
            {
                string directory = Path.GetDirectoryName(audioFilePath) ?? "";
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFilePath);
                string lrcPath = Path.Combine(directory, fileNameWithoutExt + ".lrc");
                
                File.WriteAllText(lrcPath, lrcContent);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存歌词失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LyricListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LyricListBox.SelectedItem is LyricSearchResult result)
            {
                SelectedResult = result;
                DownloadLyric = ChkDownloadLyric.IsChecked ?? false;
                
                if (DownloadLyric && !string.IsNullOrEmpty(AudioFilePath) && result.Lyric != null)
                {
                    SaveLyricToFile(AudioFilePath, result.Lyric.RawLrc);
                }
                
                this.DialogResult = true;
                this.Close();
            }
        }
    }
}
