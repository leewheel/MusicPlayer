using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace TikTokMusicPlayer
{
    public class LyricLine
    {
        public double Time { get; set; }
        public string Text { get; set; } = "";
    }

    public class LyricParser
    {
        public static List<LyricLine> ParseLrc(string lrcContent)
        {
            var lines = new List<LyricLine>();
            
            if (string.IsNullOrEmpty(lrcContent))
                return lines;

            var regex = new Regex(@"\[(\d+):(\d+)\.(\d+)\](.*)");
            
            foreach (var line in lrcContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    try
                    {
                        int minutes = int.Parse(match.Groups[1].Value);
                        int seconds = int.Parse(match.Groups[2].Value);
                        string msStr = match.Groups[3].Value;
                        
                        double milliseconds = 0;
                        if (msStr.Length >= 3)
                        {
                            milliseconds = int.Parse(msStr.Substring(0, 3)) / 1000.0;
                        }
                        else if (msStr.Length == 2)
                        {
                            milliseconds = int.Parse(msStr) / 100.0;
                        }
                        else if (msStr.Length == 1)
                        {
                            milliseconds = int.Parse(msStr) / 10.0;
                        }
                        
                        double time = minutes * 60 + seconds + milliseconds;
                        string text = match.Groups[4].Value.Trim();
                        
                        if (!string.IsNullOrEmpty(text))
                        {
                            lines.Add(new LyricLine { Time = time, Text = text });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[歌词解析] 行解析失败: {line}, 错误: {ex.Message}");
                    }
                }
            }

            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            Debug.WriteLine($"[歌词解析] 共解析出 {lines.Count} 行歌词");
            return lines;
        }

        public static int FindCurrentLineIndex(List<LyricLine> lines, double currentTime, double offset = 0)
        {
            if (lines.Count == 0)
                return -1;

            double adjustedTime = currentTime + offset;
            
            int index = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Time <= adjustedTime)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }
            
            return index;
        }

        public static string GetDisplayText(List<LyricLine> lines, int currentIndex, int contextLines = 2)
        {
            if (lines.Count == 0 || currentIndex < 0)
                return "";

            var displayText = new List<string>();
            
            int start = Math.Max(0, currentIndex - contextLines);
            int end = Math.Min(lines.Count - 1, currentIndex + contextLines);
            
            for (int i = start; i <= end; i++)
            {
                displayText.Add(lines[i].Text);
            }

            return string.Join("\n", displayText);
        }
    }
}
