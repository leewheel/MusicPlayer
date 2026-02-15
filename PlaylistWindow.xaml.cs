using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TikTokMusicPlayer
{
    public class PlaylistItem : INotifyPropertyChanged
    {
        private bool isPlaying;
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string FilePath { get; set; } = "";
        
        public bool IsPlaying
        {
            get => isPlaying;
            set
            {
                isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public partial class PlaylistWindow : Window
    {
        private MainWindow? mainWindow;
        private ObservableCollection<PlaylistItem> playlistItems = new ObservableCollection<PlaylistItem>();
        private DispatcherTimer? snapTimer;
        private bool isDragging = false;
        private bool isForceClosing = false;
        
        public event EventHandler<int>? TrackSelected;

        public PlaylistWindow()
        {
            InitializeComponent();
            PlaylistListBox.ItemsSource = playlistItems;
            
            snapTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            snapTimer.Tick += SnapTimer_Tick;
        }

        public void SetMainWindow(MainWindow window)
        {
            mainWindow = window;
            mainWindow.LocationChanged += MainWindow_LocationChanged;
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (mainWindow != null && !isDragging && this.IsVisible)
            {
                CheckAndSnapToMainWindow();
            }
        }

        private void SnapTimer_Tick(object? sender, EventArgs e)
        {
            if (!isDragging && mainWindow != null && this.IsVisible)
            {
                CheckAndSnapToMainWindow();
            }
        }

        private void CheckAndSnapToMainWindow()
        {
            if (mainWindow == null) return;

            double snapDistance = 30;
            double mainWindowRight = mainWindow.Left + mainWindow.Width;
            double mainWindowLeft = mainWindow.Left;
            double mainWindowTop = mainWindow.Top;
            double mainWindowBottom = mainWindow.Top + mainWindow.Height;

            bool shouldSnapRight = Math.Abs(this.Left - mainWindowRight - 5) < snapDistance;
            bool shouldSnapLeft = Math.Abs(this.Left + this.Width - mainWindowLeft + 5) < snapDistance;
            bool isAlignedVertically = Math.Abs(this.Top - mainWindowTop) < snapDistance;

            if ((shouldSnapRight || shouldSnapLeft) && isAlignedVertically)
            {
                if (shouldSnapRight)
                {
                    this.Left = mainWindowRight + 5;
                }
                else if (shouldSnapLeft)
                {
                    this.Left = mainWindowLeft - this.Width - 5;
                }
                this.Top = mainWindowTop;
                this.Height = mainWindow.Height;
            }
        }

        public void SetPlaylist(List<string> files, int currentIndex)
        {
            playlistItems.Clear();
            for (int i = 0; i < files.Count; i++)
            {
                playlistItems.Add(new PlaylistItem
                {
                    Index = i + 1,
                    Title = Path.GetFileNameWithoutExtension(files[i]),
                    FilePath = files[i],
                    IsPlaying = i == currentIndex
                });
            }
            TrackCountText.Text = $"({files.Count}首)";
            
            if (currentIndex >= 0 && currentIndex < playlistItems.Count)
            {
                PlaylistListBox.SelectedIndex = currentIndex;
                PlaylistListBox.ScrollIntoView(playlistItems[currentIndex]);
            }
        }

        public void UpdateCurrentTrack(int currentIndex)
        {
            for (int i = 0; i < playlistItems.Count; i++)
            {
                playlistItems[i].IsPlaying = i == currentIndex;
            }
            
            if (currentIndex >= 0 && currentIndex < playlistItems.Count)
            {
                PlaylistListBox.SelectedIndex = currentIndex;
                PlaylistListBox.ScrollIntoView(playlistItems[currentIndex]);
            }
        }

        public void ShowAtMainWindow()
        {
            if (mainWindow != null)
            {
                double mainWindowRight = mainWindow.Left + mainWindow.Width;
                this.Left = mainWindowRight + 5;
                this.Top = mainWindow.Top;
                this.Height = mainWindow.Height;
            }
            this.Show();
            this.Activate();
            snapTimer?.Start();
        }

        public void ForceClose()
        {
            isForceClosing = true;
            this.Close();
        }

        private void PlaylistListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }

        private void PlaylistListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PlaylistListBox.SelectedIndex >= 0)
            {
                TrackSelected?.Invoke(this, PlaylistListBox.SelectedIndex);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            playlistItems.Clear();
            TrackCountText.Text = "(0首)";
            mainWindow?.ClearPlaylist();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            snapTimer?.Stop();
            this.Hide();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                isDragging = true;
                this.DragMove();
                isDragging = false;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                isDragging = true;
                this.DragMove();
                isDragging = false;
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (isForceClosing)
            {
                e.Cancel = false;
                snapTimer?.Stop();
                base.OnClosing(e);
            }
            else
            {
                e.Cancel = true;
                snapTimer?.Stop();
                this.Hide();
            }
        }
    }
}
