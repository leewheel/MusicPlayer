using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TikTokMusicPlayer
{
    public class LyricSearchResult
    {
        public string Source { get; set; } = "";
        public string SongName { get; set; } = "";
        public string? Artist { get; set; }
        public LyricResult? Lyric { get; set; }
    }

    public class LyricResult
    {
        public string RawLrc { get; set; } = "";
        public List<LyricLine> Lines { get; set; } = new List<LyricLine>();
        public bool HasSyncedLyrics { get; set; }
    }

    public class LyricService
    {
        private static readonly HttpClient httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public static async Task<List<LyricSearchResult>> SearchAllLyricsAsync(string songName, string? artist = null)
        {
            var results = new List<LyricSearchResult>();
            
            string cleanSongName = CleanSearchTerm(songName);
            string cleanArtist = string.IsNullOrEmpty(artist) ? "" : CleanSearchTerm(artist);

            Debug.WriteLine($"[歌词搜索] 歌曲: {cleanSongName}, 歌手: {cleanArtist}");

            var music163Results = await TryMusic163Search(cleanSongName, cleanArtist);
            if (music163Results != null)
                results.AddRange(music163Results);

            var kugouResults = await TryKugouSearch(cleanSongName, cleanArtist);
            if (kugouResults != null)
                results.AddRange(kugouResults);

            var qqResults = await TryQQMusicSearch(cleanSongName, cleanArtist);
            if (qqResults != null)
                results.AddRange(qqResults);

            Debug.WriteLine($"[歌词搜索] 共找到 {results.Count} 个结果");
            return results;
        }

        public static async Task<LyricResult?> SearchLyricsAsync(string songName, string? artist = null)
        {
            var results = await SearchAllLyricsAsync(songName, artist);
            return results.FirstOrDefault(r => r.Lyric != null && r.Lyric.HasSyncedLyrics)?.Lyric;
        }

        private static string CleanSearchTerm(string? term)
        {
            if (string.IsNullOrEmpty(term)) return "";
            
            term = Regex.Replace(term, @"\[.*?\]", "");
            term = Regex.Replace(term, @"\(.*?\)", "");
            term = Regex.Replace(term, @"【.*?】", "");
            term = Regex.Replace(term, @"（.*?）", "");
            term = Regex.Replace(term, @"official", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"music\s*video", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"lyric\s*video", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"\bhd\b", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"\bmv\b", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"\b4k\b", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"\b1080p?\b", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"\b720p?\b", "", RegexOptions.IgnoreCase);
            term = Regex.Replace(term, @"[^\w\s\u4e00-\u9fa5\-]", " ");
            term = Regex.Replace(term, @"\s+", " ").Trim();
            return term;
        }

        private static async Task<List<LyricSearchResult>?> TryMusic163Search(string songName, string? artist)
        {
            var results = new List<LyricSearchResult>();
            try
            {
                var searchTerm = string.IsNullOrEmpty(artist) ? songName : $"{artist} {songName}";
                Debug.WriteLine($"[网易] 搜索: {searchTerm}");
                
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://music.163.com/");

                var searchUrl = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(searchTerm)}&type=1&offset=0&limit=10";
                var searchResponse = await httpClient.GetStringAsync(searchUrl);
                var searchJson = JsonDocument.Parse(searchResponse);

                if (searchJson.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("songs", out var songs) &&
                    songs.GetArrayLength() > 0)
                {
                    foreach (var song in songs.EnumerateArray())
                    {
                        if (song.TryGetProperty("id", out var idElement) &&
                            song.TryGetProperty("name", out var nameEl))
                        {
                            long songId = idElement.GetInt64();
                            string songTitle = nameEl.GetString() ?? "";
                            string? artistName = null;
                            
                            if (song.TryGetProperty("artists", out var artists) && 
                                artists.GetArrayLength() > 0)
                            {
                                artistName = artists[0].GetProperty("name").GetString();
                            }
                            
                            var lyricResult = await GetMusic163Lyrics(songId);
                            if (lyricResult != null && lyricResult.HasSyncedLyrics)
                            {
                                results.Add(new LyricSearchResult
                                {
                                    Source = "网易云音乐",
                                    SongName = songTitle,
                                    Artist = artistName,
                                    Lyric = lyricResult
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[网易] 错误: {ex.Message}");
            }
            return results.Count > 0 ? results : null;
        }

        private static async Task<LyricResult?> GetMusic163Lyrics(long songId)
        {
            try
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://music.163.com/");

                var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&tv=-1";
                var lyricResponse = await httpClient.GetStringAsync(lyricUrl);
                var lyricJson = JsonDocument.Parse(lyricResponse);

                if (lyricJson.RootElement.TryGetProperty("lrc", out var lrc) &&
                    lrc.TryGetProperty("lyric", out var lyricText))
                {
                    string? rawLrc = lyricText.GetString();
                    if (!string.IsNullOrEmpty(rawLrc))
                    {
                        return CreateLyricResult(rawLrc);
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<List<LyricSearchResult>?> TryKugouSearch(string songName, string? artist)
        {
            var results = new List<LyricSearchResult>();
            try
            {
                var searchTerm = string.IsNullOrEmpty(artist) ? songName : $"{artist} {songName}";
                Debug.WriteLine($"[酷狗] 搜索: {searchTerm}");
                
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var searchUrl = $"https://mobileservice.kugou.com/api/v3/search/song?format=json&keyword={Uri.EscapeDataString(searchTerm)}&page=1&pagesize=10&showtype=1";
                var searchResponse = await httpClient.GetStringAsync(searchUrl);
                var searchJson = JsonDocument.Parse(searchResponse);

                if (searchJson.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("info", out var info) &&
                    info.GetArrayLength() > 0)
                {
                    foreach (var song in info.EnumerateArray())
                    {
                        if (song.TryGetProperty("hash", out var hashElement) &&
                            song.TryGetProperty("filename", out var filenameEl))
                        {
                            string hash = hashElement.GetString() ?? "";
                            string filename = filenameEl.GetString() ?? "";
                            
                            if (!string.IsNullOrEmpty(hash))
                            {
                                var lyricResult = await GetKugouLyrics(hash);
                                if (lyricResult != null && lyricResult.HasSyncedLyrics)
                                {
                                    results.Add(new LyricSearchResult
                                    {
                                        Source = "酷狗音乐",
                                        SongName = filename,
                                        Lyric = lyricResult
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[酷狗] 错误: {ex.Message}");
            }
            return results.Count > 0 ? results : null;
        }

        private static async Task<LyricResult?> GetKugouLyrics(string hash)
        {
            try
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var lyricUrl = $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&tim=0&hash={hash}";
                var lyricResponse = await httpClient.GetStringAsync(lyricUrl);
                var lyricJson = JsonDocument.Parse(lyricResponse);

                if (lyricJson.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var first = candidates[0];
                    if (first.TryGetProperty("id", out var idEl) && first.TryGetProperty("accesskey", out var keyEl))
                    {
                        string id = idEl.GetInt64().ToString();
                        string accessKey = keyEl.GetString() ?? "";

                        var detailUrl = $"https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={accessKey}&fmt=lrc&charset=utf8";
                        var detailResponse = await httpClient.GetStringAsync(detailUrl);
                        var detailJson = JsonDocument.Parse(detailResponse);

                        if (detailJson.RootElement.TryGetProperty("content", out var content))
                        {
                            string base64Content = content.GetString() ?? "";
                            if (!string.IsNullOrEmpty(base64Content))
                            {
                                byte[] bytes = Convert.FromBase64String(base64Content);
                                string lrcContent = Encoding.UTF8.GetString(bytes);
                                return CreateLyricResult(lrcContent);
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<List<LyricSearchResult>?> TryQQMusicSearch(string songName, string? artist)
        {
            var results = new List<LyricSearchResult>();
            try
            {
                var searchTerm = string.IsNullOrEmpty(artist) ? songName : $"{artist} {songName}";
                Debug.WriteLine($"[QQ音乐] 搜索: {searchTerm}");
                
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://y.qq.com/");

                var searchUrl = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=10&w={Uri.EscapeDataString(searchTerm)}&format=json";
                var searchResponse = await httpClient.GetStringAsync(searchUrl);
                var searchJson = JsonDocument.Parse(searchResponse);

                if (searchJson.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("song", out var song) &&
                    song.TryGetProperty("list", out var list) &&
                    list.GetArrayLength() > 0)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.TryGetProperty("songmid", out var songmidEl) &&
                            item.TryGetProperty("songname", out var nameEl))
                        {
                            string songmid = songmidEl.GetString() ?? "";
                            string songTitle = nameEl.GetString() ?? "";
                            string? artistName = null;
                            
                            if (item.TryGetProperty("singer", out var singers) && 
                                singers.GetArrayLength() > 0)
                            {
                                artistName = singers[0].GetProperty("name").GetString();
                            }
                            
                            var lyricResult = await GetQQMusicLyrics(songmid);
                            if (lyricResult != null && lyricResult.HasSyncedLyrics)
                            {
                                results.Add(new LyricSearchResult
                                {
                                    Source = "QQ音乐",
                                    SongName = songTitle,
                                    Artist = artistName,
                                    Lyric = lyricResult
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QQ音乐] 错误: {ex.Message}");
            }
            return results.Count > 0 ? results : null;
        }

        private static async Task<LyricResult?> GetQQMusicLyrics(string songmid)
        {
            try
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://y.qq.com/");

                var lyricUrl = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={songmid}&format=json";
                var lyricResponse = await httpClient.GetStringAsync(lyricUrl);
                var lyricJson = JsonDocument.Parse(lyricResponse);

                if (lyricJson.RootElement.TryGetProperty("lyric", out var lyricEl))
                {
                    string? base64Lyric = lyricEl.GetString();
                    if (!string.IsNullOrEmpty(base64Lyric))
                    {
                        byte[] bytes = Convert.FromBase64String(base64Lyric);
                        string lrcContent = Encoding.UTF8.GetString(bytes);
                        return CreateLyricResult(lrcContent);
                    }
                }
            }
            catch { }
            return null;
        }

        private static LyricResult CreateLyricResult(string rawLrc)
        {
            var lines = LyricParser.ParseLrc(rawLrc);
            return new LyricResult
            {
                RawLrc = rawLrc,
                Lines = lines,
                HasSyncedLyrics = lines.Count > 0
            };
        }
    }
}
