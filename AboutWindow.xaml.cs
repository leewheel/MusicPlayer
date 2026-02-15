using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TikTokMusicPlayer
{
    public partial class AboutWindow : Window
    {
        private List<TextBlock> floatingChars = new List<TextBlock>();
        private readonly string floatingText = "Product By Leewheel";
        private readonly double charWidth = 16.0;
        private readonly FontFamily sciFiFont = new FontFamily("Consolas");
        private DispatcherTimer? animationTimer;
        private Stopwatch stopwatch = new Stopwatch();

        public AboutWindow()
        {
            InitializeComponent();
            Loaded += AboutWindow_Loaded;
            Closed += AboutWindow_Closed;
        }

        private void AboutWindow_Loaded(object sender, RoutedEventArgs e)
        {
            VersionText.Text = $"Version {UpdateManager.GetCurrentVersion()}";
            InitializeFloatingText();
            StartAnimation();
        }

        private void AboutWindow_Closed(object? sender, EventArgs e)
        {
            animationTimer?.Stop();
            stopwatch.Stop();
        }

        private void InitializeFloatingText()
        {
            FloatingTextCanvas.Children.Clear();
            floatingChars.Clear();

            double cx = FloatingTextCanvas.ActualWidth > 0 ? FloatingTextCanvas.ActualWidth : 300;
            double cy = FloatingTextCanvas.ActualHeight > 0 ? FloatingTextCanvas.ActualHeight : 50;

            double totalWidth = floatingText.Length * charWidth;
            double startX = (cx - totalWidth) / 2;
            double baseY = cy / 2;

            for (int i = 0; i < floatingText.Length; i++)
            {
                TextBlock tb = new TextBlock
                {
                    Text = floatingText[i].ToString(),
                    FontSize = 18,
                    FontFamily = sciFiFont,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 255))
                };

                Canvas.SetLeft(tb, startX + i * charWidth);
                Canvas.SetTop(tb, baseY);

                FloatingTextCanvas.Children.Add(tb);
                floatingChars.Add(tb);
            }
        }

        private void StartAnimation()
        {
            stopwatch.Start();
            animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            animationTimer.Tick += AnimationTimer_Tick;
            animationTimer.Start();
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            double time = stopwatch.Elapsed.TotalSeconds;
            UpdateFloatingTextAnimation(time);
        }

        private void UpdateFloatingTextAnimation(double time)
        {
            if (floatingChars.Count == 0) return;

            double cy = FloatingTextCanvas.ActualHeight > 0 ? FloatingTextCanvas.ActualHeight : 50;
            double baseY = cy / 2;

            for (int i = 0; i < floatingChars.Count; i++)
            {
                var tb = floatingChars[i];

                double waveOffset = Math.Sin(time * 2.0 + i * 0.5) * 8.0;
                Canvas.SetTop(tb, baseY + waveOffset);

                UpdateCharColor(tb);
            }
        }

        private void UpdateCharColor(TextBlock tb)
        {
            var currentColor = ((SolidColorBrush)tb.Foreground).Color;
            byte r = currentColor.R, g = currentColor.G, b = currentColor.B;

            if (b < 255 && g == 255 && r == 0)
                b++;
            else if (b == 255 && g > 0 && r == 0)
                g--;
            else if (b == 255 && r < 255 && g == 0)
                r++;
            else if (r == 255 && b > 0 && g == 0)
                b--;
            else if (r == 255 && g < 255 && b == 0)
                g++;
            else if (g == 255 && b < 255 && r == 0)
                b++;

            tb.Foreground = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
