using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;
using IOPath = System.IO.Path;

namespace TikTokMusicPlayer
{
    public class FftEventArgs : EventArgs
    {
        public FftEventArgs(Complex[] result)
        {
            Result = result;
        }
        public Complex[] Result { get; private set; }
    }

    public class SampleAggregator : ISampleProvider
    {
        public event EventHandler<FftEventArgs>? FftCalculated;
        public int NotificationCount { get; set; }
        public bool PerformFFT { get; set; }

        private readonly Complex[] fftBuffer;
        private readonly FftEventArgs fftArgs;
        private int fftPos;
        private readonly int fftLength;
        private readonly int m;
        private readonly ISampleProvider source;
        private readonly int channels;

        public SampleAggregator(ISampleProvider source, int fftLength = 1024)
        {
            this.source = source;
            channels = source.WaveFormat.Channels;

            if (!IsPowerOfTwo(fftLength))
            {
                throw new ArgumentException("FFT Length must be a power of two");
            }

            m = (int)Math.Log(fftLength, 2.0);
            this.fftLength = fftLength;
            fftBuffer = new Complex[fftLength];
            fftArgs = new FftEventArgs(fftBuffer);
        }

        static bool IsPowerOfTwo(int x)
        {
            return (x & (x - 1)) == 0;
        }

        private void Add(float value)
        {
            if (PerformFFT && FftCalculated != null)
            {
                fftBuffer[fftPos].X = (float)(value * FastFourierTransform.HammingWindow(fftPos, fftLength));
                fftBuffer[fftPos].Y = 0;
                fftPos++;
                if (fftPos >= fftBuffer.Length)
                {
                    fftPos = 0;
                    FastFourierTransform.FFT(true, m, fftBuffer);
                    FftCalculated(this, fftArgs);
                }
            }
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesRead = source.Read(buffer, offset, count);

            for (int n = 0; n < samplesRead; n += channels)
            {
                Add(buffer[n + offset]);
            }

            return samplesRead;
        }
    }

    public partial class MainWindow : Window
    {
        private Random random = new Random();
        private DispatcherTimer? animationTimer;
        private DispatcherTimer? progressTimer;

        private readonly int barCount = 32;
        private List<Rectangle> spectrumBars = new List<Rectangle>();
        private double[] lastHeights;

        private List<LyricLine> currentLyrics = new List<LyricLine>();
        private int currentLyricIndex = -1;
        private List<LyricSearchResult> lyricSearchResults = new List<LyricSearchResult>();
        private LyricSearchResult? selectedLyricResult;

        private IWavePlayer? playbackDevice;
        private WaveStream? fileStream;
        private SampleAggregator? sampleAggregator;
        private Complex[]? fftResults;
        private bool isAudioInitialized = false;
        private bool isDisposed = false;
        private bool isPlaying = false;
        private bool isDraggingSlider = false;

        private string? currentFilePath;
        private List<string> playlist = new List<string>();
        private int currentTrackIndex = -1;
        private string? albumCoverPath;

        private SettingsWindow? settingsWindow;
        private AppSettings settings = new AppSettings();

        private bool isRecording = false;
        private Process? ffmpegProcess;
        private string? recordingOutputPath;
        private string? tempVideoPath;
        private string? tempAudioPath;
        private NAudio.Wave.WasapiLoopbackCapture? audioCapture;
        private NAudio.Wave.WaveFileWriter? audioWriter;

        private PlaylistWindow? playlistWindow;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;

            lastHeights = new double[barCount];
            for (int i = 0; i < barCount; i++)
            {
                lastHeights[i] = 0;
            }

            LoadSettings();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeSpectrum();

            animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            animationTimer.Tick += AnimationTimer_Tick;
            animationTimer.Start();

            progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            progressTimer.Tick += ProgressTimer_Tick;
            progressTimer.Start();

            if (settings.AutoRecordOnPlay)
            {
                UpdateRecordingIndicator();
            }

            CheckForUpdatesAsync();
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                var updateInfo = await UpdateManager.CheckForUpdateAsync();

                if (updateInfo != null && updateInfo.HasUpdate)
                {
                    if (UpdateWindow.ShouldSkipVersion(updateInfo.LatestVersion))
                    {
                        return;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        var updateWindow = new UpdateWindow(updateInfo);
                        updateWindow.Owner = this;
                        updateWindow.ShowDialog();
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查更新失败: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            settings = AppSettings.Load();
        }

        private void SaveSettings()
        {
            settings.Save();
        }

        private void InitializeSpectrum()
        {
            SpectrumCanvas.Children.Clear();
            spectrumBars.Clear();

            double totalWidth = SpectrumCanvas.ActualWidth > 0 ? SpectrumCanvas.ActualWidth : 340;
            double barWidth = totalWidth / barCount;

            for (int i = 0; i < barCount; i++)
            {
                double hue = (double)i / barCount * 300;
                var color = HsvToRgb(hue, 1.0, 1.0);

                var barBrush = new LinearGradientBrush(
                    color,
                    Color.FromArgb(100, color.R, color.G, color.B),
                    90
                );

                var bar = new Rectangle
                {
                    Width = Math.Max(barWidth * 0.7, 2),
                    Height = 2,
                    Fill = barBrush,
                    RadiusX = 1,
                    RadiusY = 1,
                    Opacity = 0.8,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(bar, i * barWidth + (barWidth * 0.15));
                Canvas.SetBottom(bar, 0);
                SpectrumCanvas.Children.Add(bar);
                spectrumBars.Add(bar);
            }
        }

        private Color HsvToRgb(double h, double s, double v)
        {
            int hi = (int)(h / 60) % 6;
            double f = h / 60 - Math.Floor(h / 60);

            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);

            double r, g, b;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            UpdateSpectrumBars();
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            if (!isDraggingSlider && fileStream != null && playbackDevice != null)
            {
                double currentSeconds = fileStream.CurrentTime.TotalSeconds;
                double totalSeconds = fileStream.TotalTime.TotalSeconds;

                if (totalSeconds > 0)
                {
                    double progress = (currentSeconds / totalSeconds) * 100;
                    UpdateProgressUI(progress);
                    CurrentTimeText.Text = FormatTime(fileStream.CurrentTime);
                    
                    UpdateLyrics(currentSeconds);
                }
            }
        }

        private void UpdateLyrics(double currentSeconds)
        {
            if (currentLyrics.Count == 0)
                return;

            int newIndex = LyricParser.FindCurrentLineIndex(currentLyrics, currentSeconds, settings.LyricOffset);
            
            if (newIndex != currentLyricIndex && newIndex >= 0)
            {
                currentLyricIndex = newIndex;
                string displayText = LyricParser.GetDisplayText(currentLyrics, currentLyricIndex, 1);
                LyricsText.Text = displayText;
            }
        }

        private void UpdateProgressUI(double progress)
        {
            double maxWidth = ProgressTrack.ActualWidth;
            if (maxWidth <= 0) maxWidth = 250;
            
            double fillWidth = (progress / 100) * maxWidth;
            
            ProgressFill.Width = Math.Max(0, fillWidth);
            ProgressThumb.Margin = new Thickness(Math.Max(-7, fillWidth - 7), 0, 0, 0);
        }

        private string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
        }

        private void UpdateSpectrumBars()
        {
            var currentFft = fftResults;
            if (spectrumBars.Count == 0 || currentFft == null || currentFft.Length == 0)
            {
                for (int i = 0; i < spectrumBars.Count; i++)
                {
                    if (lastHeights[i] > 2)
                    {
                        lastHeights[i] -= (lastHeights[i] - 2) * 0.1;
                        spectrumBars[i].Height = lastHeights[i];
                    }
                }
                return;
            }

            int binsPerBar = currentFft.Length / (4 * spectrumBars.Count);
            double canvasHeight = SpectrumCanvas.ActualHeight > 0 ? SpectrumCanvas.ActualHeight : 80;

            for (int i = 0; i < spectrumBars.Count; i++)
            {
                double intensity = 0;
                for (int j = 0; j < binsPerBar; j++)
                {
                    int fftIndex = i * binsPerBar + j;
                    if (fftIndex < currentFft.Length / 2)
                    {
                        var c = currentFft[fftIndex];
                        intensity += Math.Sqrt(c.X * c.X + c.Y * c.Y);
                    }
                }
                intensity = (intensity / binsPerBar) * 3000;

                double targetHeight = Math.Min(intensity, canvasHeight);

                if (targetHeight > lastHeights[i])
                    lastHeights[i] = targetHeight;
                else
                    lastHeights[i] -= (lastHeights[i] - targetHeight) * 0.2;

                double newHeight = Math.Max(2, lastHeights[i]);
                if (Math.Abs(spectrumBars[i].Height - newHeight) > 0.5)
                {
                    spectrumBars[i].Height = newHeight;
                }
            }
        }

        private void SampleAggregator_FftCalculated(object? sender, FftEventArgs e)
        {
            Dispatcher.Invoke(() => fftResults = e.Result);
        }

        private void PlaybackDevice_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (isDisposed) return;

            Dispatcher.Invoke(() =>
            {
                if (isRecording)
                {
                    StopRecording();
                }

                if (playlist.Count > 0 && currentTrackIndex < playlist.Count - 1)
                {
                    PlayNextTrack();
                }
                else
                {
                    isPlaying = false;
                    BtnPlayPause.Content = "▶";
                }
            });
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            
            var itemFile = new MenuItem { Header = "选择文件" };
            itemFile.Click += (s, args) => OpenFileSelection();
            menu.Items.Add(itemFile);
            
            var itemFolder = new MenuItem { Header = "选择文件夹" };
            itemFolder.Click += (s, args) => OpenFolderSelection();
            menu.Items.Add(itemFolder);

            menu.IsOpen = true;
        }

        private void BtnAISongs_Click(object sender, RoutedEventArgs e)
        {
            string aiSongsPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "AISongs");
            
            if (!Directory.Exists(aiSongsPath))
            {
                MessageBox.Show("未找到AI翻唱歌曲目录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var audioExtensions = new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".dsf", ".dff" };
            var files = Directory.GetFiles(aiSongsPath, "*.*")
                .Where(f => audioExtensions.Contains(IOPath.GetExtension(f).ToLower()))
                .OrderBy(f => f)
                .ToList();

            if (files.Count > 0)
            {
                playlist = files;
                currentTrackIndex = 0;
                BtnPlaylist.Visibility = Visibility.Visible;
                PrepareTrack(playlist[0]);
            }
            else
            {
                MessageBox.Show("AI翻唱歌曲目录中没有找到音频文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenFileSelection()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "音频文件|*.mp3;*.flac;*.wav;*.aac;*.ogg;*.m4a;*.dsf;*.dff|所有文件|*.*",
                Multiselect = true,
                Title = "选择音乐文件"
            };

            if (dialog.ShowDialog() == true)
            {
                playlist = dialog.FileNames.ToList();
                currentTrackIndex = 0;
                if (playlist.Count > 1)
                {
                    BtnPlaylist.Visibility = Visibility.Visible;
                }
                else
                {
                    BtnPlaylist.Visibility = Visibility.Collapsed;
                }
                PrepareTrack(playlist[0]);
            }
        }

        private void PrepareTrack(string filePath)
        {
            try
            {
                StopPlayback();
                
                currentFilePath = filePath;
                SongTitleText.Text = IOPath.GetFileNameWithoutExtension(filePath);

                string extension = IOPath.GetExtension(filePath).ToLower();
                string formatText = GetAudioFormatDisplay(extension);
                AudioFormatText.Text = formatText;
                AudioFormatText.Visibility = string.IsNullOrEmpty(formatText) ? Visibility.Collapsed : Visibility.Visible;

                LoadAlbumCover(filePath);
                
                currentLyrics.Clear();
                currentLyricIndex = -1;
                LyricsText.Text = "";
                BtnSelectLyric.Visibility = Visibility.Collapsed;
                
                BtnPlayPause.Content = "▶";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplySelectedLyric()
        {
            if (selectedLyricResult?.Lyric != null)
            {
                currentLyrics = selectedLyricResult.Lyric.Lines;
                currentLyricIndex = -1;
                
                if (currentLyrics.Count > 0)
                {
                    LyricsText.Text = currentLyrics[0].Text;
                }
            }
        }

        private void BtnSelectLyric_Click(object sender, RoutedEventArgs e)
        {
            var selectionWindow = new LyricSelectionWindow();
            selectionWindow.Owner = this;
            selectionWindow.AudioFilePath = currentFilePath;
            selectionWindow.SetResults(lyricSearchResults);

            if (selectionWindow.ShowDialog() == true && selectionWindow.SelectedResult != null)
            {
                selectedLyricResult = selectionWindow.SelectedResult;
                ApplySelectedLyric();
            }
        }

        private void BtnPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (playlistWindow == null)
            {
                playlistWindow = new PlaylistWindow();
                playlistWindow.SetMainWindow(this);
                playlistWindow.TrackSelected += PlaylistWindow_TrackSelected;
            }

            playlistWindow.SetPlaylist(playlist, currentTrackIndex);
            playlistWindow.ShowAtMainWindow();
        }

        private void PlaylistWindow_TrackSelected(object? sender, int index)
        {
            if (index >= 0 && index < playlist.Count)
            {
                currentTrackIndex = index;
                PrepareTrack(playlist[index]);
                PlayTrack(playlist[index]);
                playlistWindow?.UpdateCurrentTrack(currentTrackIndex);
            }
        }

        public void ClearPlaylist()
        {
            playlist.Clear();
            currentTrackIndex = -1;
            StopPlayback();
            SongTitleText.Text = "未选择音乐";
            LyricsText.Text = "";
            AlbumCoverImage.Source = null;
        }

        private void OpenFolderSelection()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择音乐文件夹"
            };

            if (dialog.ShowDialog() == true)
            {
                var audioExtensions = new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".dsf", ".dff" };
                var files = Directory.GetFiles(dialog.FolderName, "*.*")
                    .Where(f => audioExtensions.Contains(IOPath.GetExtension(f).ToLower()))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count > 0)
                {
                    playlist = files;
                    currentTrackIndex = 0;
                    BtnPlaylist.Visibility = Visibility.Visible;
                    PrepareTrack(playlist[0]);
                }
                else
                {
                    MessageBox.Show("该文件夹中没有找到音频文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void PlayTrack(string filePath)
        {
            try
            {
                StopPlayback();

                currentFilePath = filePath;

                string extension = IOPath.GetExtension(filePath).ToLower();

                try
                {
                    var audioFileReader = new AudioFileReader(filePath);
                    fileStream = audioFileReader;
                }
                catch
                {
                    switch (extension)
                    {
                        case ".mp3":
                            fileStream = new Mp3FileReader(filePath);
                            break;
                        case ".wav":
                            fileStream = new WaveFileReader(filePath);
                            break;
                        case ".dsf":
                        case ".dff":
                            fileStream = new FFmpegAudioReader(filePath);
                            break;
                        default:
                            throw new NotSupportedException($"不支持的音频格式: {extension}");
                    }
                }

                var sampleProvider = fileStream.ToSampleProvider();
                sampleAggregator = new SampleAggregator(sampleProvider, 1024);
                sampleAggregator.NotificationCount = 512;
                sampleAggregator.PerformFFT = true;
                sampleAggregator.FftCalculated += SampleAggregator_FftCalculated;

                playbackDevice = new WaveOutEvent { DesiredLatency = 100 };
                playbackDevice.PlaybackStopped += PlaybackDevice_PlaybackStopped;
                playbackDevice.Init(sampleAggregator);
                playbackDevice.Play();

                isAudioInitialized = true;
                isPlaying = true;
                BtnPlayPause.Content = "⏸";

                SongTitleText.Text = IOPath.GetFileNameWithoutExtension(filePath);
                TotalTimeText.Text = FormatTime(fileStream.TotalTime);

                string formatText = GetAudioFormatDisplay(extension);
                AudioFormatText.Text = formatText;
                AudioFormatText.Visibility = string.IsNullOrEmpty(formatText) ? Visibility.Collapsed : Visibility.Visible;

                SearchLyricsAsync(filePath);
                BtnSelectLyric.Visibility = Visibility.Collapsed;
                LyricsText.Text = "正在搜索歌词...";

                if (settings.AutoRecordOnPlay && !isRecording)
                {
                    try
                    {
                        StartRecording();
                    }
                    catch (Exception recEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"自动录制启动失败: {recEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"播放失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopPlayback()
        {
            if (playbackDevice != null)
            {
                playbackDevice.PlaybackStopped -= PlaybackDevice_PlaybackStopped;
                playbackDevice.Stop();
                playbackDevice.Dispose();
                playbackDevice = null;
            }

            if (fileStream != null)
            {
                fileStream.Dispose();
                fileStream = null;
            }

            if (sampleAggregator != null)
            {
                sampleAggregator.FftCalculated -= SampleAggregator_FftCalculated;
                sampleAggregator = null;
            }

            isAudioInitialized = false;
            fftResults = null;
        }

        private string GetAudioFormatDisplay(string extension)
        {
            return extension.ToLower() switch
            {
                ".mp3" => "MP3",
                ".flac" => "FLAC",
                ".wav" => "WAV",
                ".aac" => "AAC",
                ".ogg" => "OGG",
                ".m4a" => "M4A",
                ".dsf" => "DSD",
                ".dff" => "DSD",
                ".wma" => "WMA",
                ".ape" => "APE",
                _ => extension.TrimStart('.').ToUpper()
            };
        }

        private void LoadAlbumCover(string audioFilePath)
        {
            string directory = IOPath.GetDirectoryName(audioFilePath) ?? "";
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

            var imageFiles = Directory.GetFiles(directory, "*.*")
                .Where(f => imageExtensions.Contains(IOPath.GetExtension(f).ToLower()))
                .FirstOrDefault();

            if (imageFiles != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imageFiles);
                    bitmap.EndInit();
                    AlbumCoverImage.Source = bitmap;
                    albumCoverPath = imageFiles;
                }
                catch
                {
                    SetDefaultAlbumCover();
                }
            }
            else
            {
                SetDefaultAlbumCover();
            }
        }

        private void SetDefaultAlbumCover()
        {
            AlbumCoverImage.Source = null;
        }

        private async void SearchLyricsAsync(string filePath)
        {
            currentLyrics.Clear();
            currentLyricIndex = -1;
            lyricSearchResults.Clear();
            
            string directory = IOPath.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = IOPath.GetFileNameWithoutExtension(filePath);

            string lrcPath = IOPath.Combine(directory, fileNameWithoutExt + ".lrc");

            if (File.Exists(lrcPath))
            {
                try
                {
                    string lrcContent = File.ReadAllText(lrcPath);
                    currentLyrics = LyricParser.ParseLrc(lrcContent);
                    if (currentLyrics.Count > 0)
                    {
                        LyricsText.Text = currentLyrics[0].Text;
                        BtnSelectLyric.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
                catch { }
            }

            LyricsText.Text = "正在搜索歌词...";

            string songName = fileNameWithoutExt;
            string? artist = null;

            var parts = songName.Split(new[] { " - ", " – ", "-", "–" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                artist = parts[0].Trim();
                songName = parts[1].Trim();
            }

            try
            {
                var results = await LyricService.SearchAllLyricsAsync(songName, artist);
                lyricSearchResults = results;

                if (results.Count > 0)
                {
                    if (results.Count == 1)
                    {
                        selectedLyricResult = results[0];
                        ApplySelectedLyric();
                        BtnSelectLyric.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        BtnSelectLyric.Visibility = Visibility.Visible;
                        selectedLyricResult = results[0];
                        ApplySelectedLyric();
                        LyricsText.Text = $"找到 {results.Count} 个歌词结果\n点击右侧按钮选择";
                    }
                }
                else
                {
                    LyricsText.Text = "未找到歌词";
                    BtnSelectLyric.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                LyricsText.Text = $"歌词搜索失败: {ex.Message}";
                BtnSelectLyric.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (fileStream == null || playbackDevice == null)
            {
                if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
                {
                    PlayTrack(currentFilePath);
                }
                else if (playlist.Count > 0 && currentTrackIndex >= 0)
                {
                    PlayTrack(playlist[currentTrackIndex]);
                }
                else
                {
                    OpenFileSelection();
                }
                return;
            }

            if (isPlaying)
            {
                playbackDevice.Pause();
                isPlaying = false;
                BtnPlayPause.Content = "▶";
            }
            else
            {
                playbackDevice.Play();
                isPlaying = true;
                BtnPlayPause.Content = "⏸";
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (playlist.Count > 0 && currentTrackIndex > 0)
            {
                currentTrackIndex--;
                PlayTrack(playlist[currentTrackIndex]);
                playlistWindow?.UpdateCurrentTrack(currentTrackIndex);
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            PlayNextTrack();
        }

        private void PlayNextTrack()
        {
            if (playlist.Count > 0 && currentTrackIndex < playlist.Count - 1)
            {
                currentTrackIndex++;
                PlayTrack(playlist[currentTrackIndex]);
                playlistWindow?.UpdateCurrentTrack(currentTrackIndex);
            }
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }

        private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            isDraggingSlider = true;
            ProgressGrid.CaptureMouse();
            UpdateProgressFromMouse(e);
            e.Handled = true;
        }

        private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingSlider)
            {
                if (fileStream != null)
                {
                    double totalSeconds = fileStream.TotalTime.TotalSeconds;
                    double progress = GetProgressFromMouse(e);
                    double targetSeconds = (progress / 100) * totalSeconds;
                    fileStream.CurrentTime = TimeSpan.FromSeconds(targetSeconds);
                }
                isDraggingSlider = false;
                ProgressGrid.ReleaseMouseCapture();
            }
            e.Handled = true;
        }

        private void ProgressSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingSlider && fileStream != null)
            {
                double progress = GetProgressFromMouse(e);
                UpdateProgressUI(progress);
                double totalSeconds = fileStream.TotalTime.TotalSeconds;
                double targetSeconds = (progress / 100) * totalSeconds;
                CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(targetSeconds));
            }
        }

        private void ProgressGrid_LostMouseCapture(object sender, MouseEventArgs e)
        {
            isDraggingSlider = false;
        }

        private double GetProgressFromMouse(MouseEventArgs e)
        {
            Point position = e.GetPosition(ProgressTrack);
            return Math.Max(0, Math.Min(100, (position.X / ProgressTrack.ActualWidth) * 100));
        }

        private void UpdateProgressFromMouse(MouseButtonEventArgs e)
        {
            double progress = GetProgressFromMouse(e);
            UpdateProgressUI(progress);
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void WindowBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void OpenSettingsWindow()
        {
            if (settingsWindow == null || !settingsWindow.IsLoaded)
            {
                settingsWindow = new SettingsWindow(settings);
                settingsWindow.SettingsChanged += SettingsWindow_SettingsChanged;
                settingsWindow.Show();
            }
            else
            {
                settingsWindow.Activate();
            }
        }

        private void SettingsWindow_SettingsChanged(object? sender, AppSettings e)
        {
            settings = e;
            SaveSettings();
            UpdateRecordingIndicator();
        }

        private void UpdateRecordingIndicator()
        {
            if (isRecording)
            {
                AlbumCoverBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C77DFF"));
                AlbumCoverBorder.Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#C77DFF"),
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
            }
            else
            {
                AlbumCoverBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FFFF"));
                AlbumCoverBorder.Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#00FFFF"),
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }
        }

        private void StartRecording()
        {
            if (!IsFFmpegAvailable())
            {
                MessageBox.Show("未找到FFmpeg，无法录制视频。请安装FFmpeg并添加到系统PATH。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outputDir = settings.RecordingOutputPath;
            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            {
                outputDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TikTokMusicPlayer");
            }
            
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception dirEx)
            {
                MessageBox.Show($"无法创建录制目录: {dirEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = settings.RecordingFileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"recording_{timestamp}";
            }
            
            string tempDir = IOPath.Combine(IOPath.GetTempPath(), "TikTokMusicPlayer_Recording");
            Directory.CreateDirectory(tempDir);
            tempVideoPath = IOPath.Combine(tempDir, $"video_{timestamp}.mp4");
            tempAudioPath = IOPath.Combine(tempDir, $"audio_{timestamp}.wav");
            recordingOutputPath = IOPath.Combine(outputDir, $"{fileName}.mp4");

            int width = (int)this.ActualWidth;
            int height = (int)this.ActualHeight;

            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            var windowBounds = GetWindowBounds();
            
            string ffmpegExe = GetFFmpegPath();

            string ffmpegArgs = $"-f gdigrab -framerate 30 " +
                               $"-offset_x {windowBounds.X} -offset_y {windowBounds.Y} " +
                               $"-video_size {width}x{height} " +
                               $"-i desktop " +
                               $"-c:v libx264 -preset ultrafast -crf 23 " +
                               $"-pix_fmt yuv420p -y \"{tempVideoPath}\"";

            System.Diagnostics.Debug.WriteLine($"[录制] FFmpeg路径: {ffmpegExe}");
            System.Diagnostics.Debug.WriteLine($"[录制] FFmpeg参数: {ffmpegArgs}");
            System.Diagnostics.Debug.WriteLine($"[录制] 临时视频: {tempVideoPath}");
            System.Diagnostics.Debug.WriteLine($"[录制] 临时音频: {tempAudioPath}");

            try
            {
                var defaultDevice = NAudio.Wave.WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
                if (defaultDevice != null)
                {
                    audioCapture = new NAudio.Wave.WasapiLoopbackCapture(defaultDevice);
                    audioWriter = new NAudio.Wave.WaveFileWriter(tempAudioPath, audioCapture.WaveFormat);
                    
                    audioCapture.DataAvailable += (s, e) =>
                    {
                        if (audioWriter != null && e.BytesRecorded > 0)
                        {
                            audioWriter.Write(e.Buffer, 0, e.BytesRecorded);
                        }
                    };
                    
                    audioCapture.StartRecording();
                    System.Diagnostics.Debug.WriteLine("[录制] 音频捕获已启动");
                }

                ffmpegProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegExe,
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true
                    }
                };

                ffmpegProcess.ErrorDataReceived += (s, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        System.Diagnostics.Debug.WriteLine($"[FFmpeg] {e.Data}");
                };
                
                ffmpegProcess.Start();
                ffmpegProcess.BeginErrorReadLine();
                isRecording = true;
                UpdateRecordingIndicator();
                
                System.Diagnostics.Debug.WriteLine($"[录制] 开始录制，PID: {ffmpegProcess.Id}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动录制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                CleanupRecording();
            }
        }

        private string GetFFmpegPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localFFmpeg = IOPath.Combine(appDir, "ffmpeg", "ffmpeg.exe");
            
            if (File.Exists(localFFmpeg))
            {
                return localFFmpeg;
            }
            
            return "ffmpeg";
        }

        private (int X, int Y) GetWindowBounds()
        {
            var presentationSource = PresentationSource.FromVisual(this);
            if (presentationSource != null)
            {
                var transform = presentationSource.CompositionTarget.TransformToDevice;
                double dpiScaleX = transform.M11;
                double dpiScaleY = transform.M22;
                
                int x = (int)(this.Left * dpiScaleX);
                int y = (int)(this.Top * dpiScaleY);
                
                return (x, y);
            }
            return ((int)this.Left, (int)this.Top);
        }

        private void StopRecording()
        {
            System.Diagnostics.Debug.WriteLine("[录制] 正在停止录制...");

            if (audioCapture != null)
            {
                try
                {
                    audioCapture.StopRecording();
                    audioCapture.Dispose();
                    audioCapture = null;
                    System.Diagnostics.Debug.WriteLine("[录制] 音频捕获已停止");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[录制] 停止音频捕获异常: {ex.Message}");
                }
            }

            if (audioWriter != null)
            {
                try
                {
                    audioWriter.Dispose();
                    audioWriter = null;
                }
                catch { }
            }

            if (ffmpegProcess != null && !ffmpegProcess.HasExited)
            {
                try
                {
                    ffmpegProcess.StandardInput.WriteLine("q");
                    bool exited = ffmpegProcess.WaitForExit(5000);
                    if (!exited)
                    {
                        System.Diagnostics.Debug.WriteLine("[录制] FFmpeg未响应，强制终止");
                        ffmpegProcess.Kill();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[录制] FFmpeg已退出，代码: {ffmpegProcess.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[录制] 停止FFmpeg异常: {ex.Message}");
                }

                ffmpegProcess.Dispose();
                ffmpegProcess = null;
            }

            MergeAudioVideo();

            isRecording = false;
            UpdateRecordingIndicator();
        }

        private void MergeAudioVideo()
        {
            if (string.IsNullOrEmpty(tempVideoPath) || string.IsNullOrEmpty(recordingOutputPath))
            {
                CleanupRecording();
                return;
            }

            try
            {
                string ffmpegExe = GetFFmpegPath();
                bool hasAudio = File.Exists(tempAudioPath) && new FileInfo(tempAudioPath).Length > 1000;

                string mergeArgs;
                if (hasAudio)
                {
                    mergeArgs = $"-i \"{tempVideoPath}\" -i \"{tempAudioPath}\" " +
                               $"-c:v copy -c:a aac -b:a 192k -shortest " +
                               $"-movflags +faststart -y \"{recordingOutputPath}\"";
                }
                else
                {
                    mergeArgs = $"-i \"{tempVideoPath}\" -c:v copy " +
                               $"-movflags +faststart -y \"{recordingOutputPath}\"";
                }

                System.Diagnostics.Debug.WriteLine($"[录制] 合并参数: {mergeArgs}");

                var mergeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegExe,
                        Arguments = mergeArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                mergeProcess.Start();
                mergeProcess.WaitForExit(30000);

                if (File.Exists(recordingOutputPath))
                {
                    var fileInfo = new FileInfo(recordingOutputPath);
                    System.Diagnostics.Debug.WriteLine($"[录制] 文件已生成: {recordingOutputPath}, 大小: {fileInfo.Length / 1024}KB");
                    
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"录制完成！\n文件: {recordingOutputPath}\n大小: {fileInfo.Length / 1024 / 1024:F2} MB", 
                            "录制完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[录制] 文件未生成: {recordingOutputPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[录制] 合并异常: {ex.Message}");
            }
            finally
            {
                CleanupRecording();
            }
        }

        private void CleanupRecording()
        {
            try
            {
                if (!string.IsNullOrEmpty(tempVideoPath) && File.Exists(tempVideoPath))
                {
                    File.Delete(tempVideoPath);
                }
                if (!string.IsNullOrEmpty(tempAudioPath) && File.Exists(tempAudioPath))
                {
                    File.Delete(tempAudioPath);
                }
            }
            catch { }

            tempVideoPath = null;
            tempAudioPath = null;
        }

        private bool IsFFmpegAvailable()
        {
            string ffmpegPath = GetFFmpegPath();
            
            if (File.Exists(ffmpegPath))
            {
                return true;
            }
            
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F9 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                OpenSettingsWindow();
                e.Handled = true;
            }
            else if (e.Key == Key.OemComma)
            {
                AdjustLyricOffset(-0.5);
                e.Handled = true;
            }
            else if (e.Key == Key.OemPeriod)
            {
                AdjustLyricOffset(0.5);
                e.Handled = true;
            }
        }

        private void AdjustLyricOffset(double delta)
        {
            settings.LyricOffset += delta;
            settings.Save();
            
            double offset = settings.LyricOffset;
            string offsetText = offset >= 0 ? $"+{offset:F1}s" : $"{offset:F1}s";
            
            if (currentLyrics.Count > 0)
            {
                LyricsText.Text = $"歌词偏移: {offsetText}\n{LyricParser.GetDisplayText(currentLyrics, Math.Max(0, currentLyricIndex), 1)}";
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            isDisposed = true;

            if (isRecording)
            {
                StopRecording();
            }

            StopPlayback();

            if (animationTimer != null)
            {
                animationTimer.Stop();
                animationTimer.Tick -= AnimationTimer_Tick;
            }

            if (progressTimer != null)
            {
                progressTimer.Stop();
                progressTimer.Tick -= ProgressTimer_Tick;
            }

            if (playlistWindow != null)
            {
                playlistWindow.ForceClose();
            }

            SpectrumCanvas?.Children.Clear();
            spectrumBars?.Clear();
        }
    }
}
