using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Updater
{
    public partial class MainWindow : Window
    {
        private string? sourceDir;
        private string? targetDir;
        private int copiedFiles = 0;
        private int totalFiles = 0;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        public MainWindow(string source, string target) : this()
        {
            sourceDir = source;
            targetDir = target;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                
                if (args.Length >= 3)
                {
                    sourceDir = args[1].Trim('"');
                    targetDir = args[2].Trim('"');
                }

                if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(targetDir))
                {
                    Log("错误: 缺少参数");
                    StatusText.Text = "更新失败";
                    return;
                }

                await RunUpdateAsync();
            }
            catch (Exception ex)
            {
                Log($"错误: {ex.Message}");
                StatusText.Text = "更新失败";
            }
        }

        private async Task RunUpdateAsync()
        {
            try
            {
                Log($"源目录: {sourceDir}");
                Log($"目标目录: {targetDir}");

                StatusText.Text = "等待主程序关闭...";
                Log("等待主程序关闭...");
                await Task.Delay(2000);

                StatusText.Text = "正在关闭主程序...";
                Log("正在关闭主程序...");
                await CloseMainAppAsync();

                StatusText.Text = "正在备份旧文件...";
                Log("正在备份旧文件...");
                await BackupAndReplaceAsync();

                StatusText.Text = "正在清理临时文件...";
                Log("正在清理临时文件...");
                CleanupTempFiles();

                StatusText.Text = "更新完成!";
                Log("更新完成!");
                ProgressBar.Value = 100;
                ProgressText.Text = "100%";

                await Task.Delay(1500);

                StatusText.Text = "正在启动主程序...";
                Log("正在启动主程序...");
                StartMainApp();

                await Task.Delay(1000);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log($"更新失败: {ex.Message}");
                StatusText.Text = "更新失败";
                MessageBox.Show($"更新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CloseMainAppAsync()
        {
            try
            {
                var processes = Process.GetProcessesByName("TikTokMusicPlayer");
                foreach (var process in processes)
                {
                    Log($"正在关闭进程: {process.ProcessName} (PID: {process.Id})");
                    process.CloseMainWindow();
                    
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                        Log("强制终止进程");
                    }
                    process.Dispose();
                }
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Log($"关闭主程序时出错: {ex.Message}");
            }
        }

        private async Task BackupAndReplaceAsync()
        {
            if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(targetDir))
                return;

            totalFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories).Length;
            copiedFiles = 0;

            await Task.Run(() =>
            {
                CopyDirectory(sourceDir, targetDir);
            });
        }

        private void CopyDirectory(string source, string target)
        {
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
            }

            foreach (string file in Directory.GetFiles(source))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(target, fileName);

                try
                {
                    if (File.Exists(destFile))
                    {
                        string backupFile = destFile + ".bak";
                        if (File.Exists(backupFile))
                        {
                            File.Delete(backupFile);
                        }
                        File.Move(destFile, backupFile);
                    }

                    File.Copy(file, destFile, true);
                    copiedFiles++;

                    Dispatcher.Invoke(() =>
                    {
                        int progress = totalFiles > 0 ? (int)((double)copiedFiles / totalFiles * 100) : 0;
                        ProgressBar.Value = progress;
                        ProgressText.Text = $"{progress}%";
                        Log($"复制: {fileName}");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log($"复制 {fileName} 失败: {ex.Message}"));
                }
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                string dirName = Path.GetFileName(dir);
                string destDir = Path.Combine(target, dirName);
                CopyDirectory(dir, destDir);
            }
        }

        private void CleanupTempFiles()
        {
            try
            {
                if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
                {
                    Directory.Delete(sourceDir, true);
                    Log("已删除临时解压目录");
                }

                string downloadDir = Path.Combine(Path.GetTempPath(), "TikTokMusicPlayerUpdate");
                if (Directory.Exists(downloadDir))
                {
                    Directory.Delete(downloadDir, true);
                    Log("已删除下载目录");
                }
            }
            catch (Exception ex)
            {
                Log($"清理临时文件时出错: {ex.Message}");
            }
        }

        private void StartMainApp()
        {
            try
            {
                if (string.IsNullOrEmpty(targetDir))
                    return;

                string exePath = Path.Combine(targetDir, "TikTokMusicPlayer.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WorkingDirectory = targetDir
                    });
                    Log("已启动主程序");
                }
                else
                {
                    Log($"未找到主程序: {exePath}");
                }
            }
            catch (Exception ex)
            {
                Log($"启动主程序时出错: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogTextBox.ScrollToEnd();
            });
        }
    }
}
