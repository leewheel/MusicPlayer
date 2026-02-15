using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace TikTokMusicPlayer
{
    public class FFmpegAudioReader : WaveStream
    {
        private Process? ffmpegProcess;
        private Stream? outputStream;
        private BinaryReader? reader;
        private WaveFormat waveFormat;
        private long length;
        private long position;
        private string filePath;
        private bool isDisposed;

        public FFmpegAudioReader(string filePath)
        {
            this.filePath = filePath;
            
            string ffmpegPath = GetFFmpegPath();
            if (!File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException("FFmpeg not found", ffmpegPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{filePath}\" -f s16le -acodec pcm_s16le -ar 44100 -ac 2 -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            ffmpegProcess = new Process { StartInfo = startInfo };
            ffmpegProcess.Start();
            
            outputStream = new MemoryStream();
            ffmpegProcess.StandardOutput.BaseStream.CopyTo(outputStream);
            ffmpegProcess.WaitForExit(10000);
            
            outputStream.Position = 0;
            reader = new BinaryReader(outputStream);
            
            waveFormat = new WaveFormat(44100, 16, 2);
            length = outputStream.Length;
            position = 0;
        }

        private string GetFFmpegPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localFFmpeg = Path.Combine(appDir, "ffmpeg", "ffmpeg.exe");
            
            if (File.Exists(localFFmpeg))
            {
                return localFFmpeg;
            }
            
            return "ffmpeg";
        }

        public override WaveFormat WaveFormat => waveFormat;

        public override long Length => length;

        public override long Position
        {
            get => position;
            set
            {
                if (outputStream != null)
                {
                    outputStream.Position = value;
                    position = value;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (reader == null) return 0;
            
            int bytesRead = reader.Read(buffer, offset, count);
            position += bytesRead;
            return bytesRead;
        }

        public override TimeSpan TotalTime
        {
            get
            {
                if (waveFormat.AverageBytesPerSecond > 0)
                {
                    return TimeSpan.FromSeconds((double)length / waveFormat.AverageBytesPerSecond);
                }
                return TimeSpan.Zero;
            }
        }

        public override TimeSpan CurrentTime
        {
            get
            {
                if (waveFormat.AverageBytesPerSecond > 0)
                {
                    return TimeSpan.FromSeconds((double)position / waveFormat.AverageBytesPerSecond);
                }
                return TimeSpan.Zero;
            }
            set
            {
                Position = (long)(value.TotalSeconds * waveFormat.AverageBytesPerSecond);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    reader?.Dispose();
                    outputStream?.Dispose();
                    
                    if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                    {
                        ffmpegProcess.Kill();
                    }
                    ffmpegProcess?.Dispose();
                }
                isDisposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
