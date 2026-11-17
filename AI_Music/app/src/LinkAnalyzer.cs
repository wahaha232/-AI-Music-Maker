using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AIMusicGenerator
{
    internal sealed class LinkInfo
    {
        public string Source = "";
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public string Year = "";
        public string Description = "";
    }

    internal sealed class PromptSuggestion
    {
        public string Genre = "";
        public string Bpm = "";
        public string Prompt = "";
        public string Negative = "";
    }

    internal static class LinkAnalyzer
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        static LinkAnalyzer()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception)
            {
            }
        }

        public static LinkInfo Analyze(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
                throw new ArgumentException("請輸入完整的 http/https 連結。");
            string host = uri.Host.ToLowerInvariant();
            if (host.Contains("youtube.com") || host.Contains("youtu.be"))
                return AnalyzeYouTube(url.Trim());
            if (host.Contains("spotify.com"))
                return AnalyzeSpotify(url.Trim());
            return AnalyzeGeneric(url.Trim());
        }

        private static LinkInfo AnalyzeYouTube(string url)
        {
            string endpoint = "https://www.youtube.com/oembed?url=" + Uri.EscapeDataString(url) + "&format=json";
            Dictionary<string, object> data = ParseJson(Fetch(endpoint));
            string title = GetString(data, "title");
            string author = GetString(data, "author_name");
            if (author.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
                author = author.Substring(0, author.Length - 8).Trim();
            LinkInfo info = new LinkInfo();
            info.Source = "YouTube";
            info.Title = CleanTitle(title);
            info.Artist = author;
            if (info.Artist.Length > 0)
            {
                string prefix = info.Artist + " - ";
                if (info.Title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    info.Title = info.Title.Substring(prefix.Length).Trim();
            }
            info.Description = title;
            return info;
        }

        private static LinkInfo AnalyzeSpotify(string url)
        {
            LinkInfo info = new LinkInfo();
            info.Source = "Spotify";
            try
            {
                string endpoint = "https://open.spotify.com/oembed?url=" + Uri.EscapeDataString(url);
                Dictionary<string, object> data = ParseJson(Fetch(endpoint));
                info.Title = GetString(data, "title");
                info.Description = info.Title;
            }
            catch (Exception)
            {
            }
            try
            {
                string html = Fetch(url);
                Match desc = Regex.Match(html,
                    "<meta[^>]+property=\"og:description\"[^>]+content=\"([^\"]*)\"",
                    RegexOptions.IgnoreCase);
                if (desc.Success)
                {
                    string value = WebUtility.HtmlDecode(desc.Groups[1].Value);
                    string[] parts = value.Split(new string[] { " · " }, StringSplitOptions.None);
                    if (parts.Length >= 1) info.Artist = parts[0].Trim();
                    if (parts.Length >= 2) info.Album = parts[1].Trim();
                    if (parts.Length >= 2) info.Year = parts[parts.Length - 1].Trim();
                    info.Description = value;
                }
            }
            catch (Exception)
            {
            }
            if (info.Title.Length == 0)
            {
                try
                {
                    string html = Fetch(url);
                    Match title = Regex.Match(html, "<title>([^<]*)</title>", RegexOptions.IgnoreCase);
                    if (title.Success)
                    {
                        string pageTitle = WebUtility.HtmlDecode(title.Groups[1].Value).Trim();
                        int marker = pageTitle.IndexOf(" - song", StringComparison.OrdinalIgnoreCase);
                        if (marker > 0) info.Title = pageTitle.Substring(0, marker).Trim();
                        else info.Title = pageTitle;
                        int by = pageTitle.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
                        if (info.Artist.Length == 0 && by > 0)
                        {
                            string tail = pageTitle.Substring(by + 4);
                            int bar = tail.IndexOf('|');
                            info.Artist = (bar > 0 ? tail.Substring(0, bar) : tail).Trim();
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return info;
        }

        private static LinkInfo AnalyzeGeneric(string url)
        {
            LinkInfo info = new LinkInfo();
            info.Source = "Web";
            string html = Fetch(url);
            Match title = Regex.Match(html, "<meta[^>]+property=\"og:title\"[^>]+content=\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            if (!title.Success)
                title = Regex.Match(html, "<title>([^<]*)</title>", RegexOptions.IgnoreCase);
            if (title.Success) info.Title = WebUtility.HtmlDecode(title.Groups[1].Value).Trim();
            Match desc = Regex.Match(html, "<meta[^>]+property=\"og:description\"[^>]+content=\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            if (desc.Success) info.Description = WebUtility.HtmlDecode(desc.Groups[1].Value).Trim();
            return info;
        }

        private static bool ContainsKeyword(string text, string keyword)
        {
            if (Regex.IsMatch(keyword, "^[a-z0-9]+$"))
                return Regex.IsMatch(text, "\\b" + Regex.Escape(keyword) + "\\b");
            return text.Contains(keyword);
        }

        public static PromptSuggestion Suggest(LinkInfo info, bool instrumental, bool chinese = false)
        {
            string haystack = (info.Title + " " + info.Artist + " " + info.Album + " " + info.Description)
                .ToLowerInvariant();
            string titleArtist = (info.Title + " " + info.Artist).ToLowerInvariant();

            List<Dictionary<string, object>> genres = LoadGenres();
            Dictionary<string, object> best = null;
            int bestScore = 0;
            foreach (Dictionary<string, object> genre in genres)
            {
                int score = 0;
                object keywordsValue;
                if (genre.TryGetValue("keywords", out keywordsValue) && keywordsValue is IEnumerable)
                {
                    foreach (object keywordObject in (IEnumerable)keywordsValue)
                    {
                        string keyword = Convert.ToString(keywordObject).ToLowerInvariant();
                        if (keyword.Length == 0) continue;
                        if (ContainsKeyword(titleArtist, keyword)) score += 3;
                        else if (ContainsKeyword(haystack, keyword)) score += 1;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = genre;
                }
            }
            if (best == null)
            {
                foreach (Dictionary<string, object> genre in genres)
                {
                    if (GetString(genre, "name") == "pop")
                    {
                        best = genre;
                        break;
                    }
                }
            }
            if (best == null && genres.Count > 0) best = genres[0];

            PromptSuggestion suggestion = new PromptSuggestion();
            if (best == null)
            {
                suggestion.Genre = "instrumental";
                suggestion.Prompt = "Instrumental music with a clear mood and production style. ";
                suggestion.Negative = "low quality, clipping, muddy mix";
                return suggestion;
            }

            suggestion.Genre = chinese ? GetString(best, "name_zh") : GetString(best, "name");
            if (suggestion.Genre.Length == 0) suggestion.Genre = GetString(best, "name");
            suggestion.Bpm = GetString(best, "bpm");
            string prompt = (chinese ? GetString(best, "prompt_zh") : GetString(best, "prompt")).Trim();
            if (chinese)
            {
                if (prompt.Length > 0 && !prompt.EndsWith("。")) prompt += "。";
                prompt += " ";
                if (info.Artist.Length > 0 || info.Title.Length > 0)
                {
                    prompt += "風格參考：" + info.Artist;
                    if (info.Title.Length > 0) prompt += " - " + info.Title;
                    prompt += "（僅參考風格，不是翻唱，也不複製旋律）。";
                }
                if (instrumental)
                    prompt += "純音樂。完全無人聲、無歌唱、無饒舌、無朗讀、無合唱、無哼唱、無人聲切片。";
                prompt += "高品質製作，編曲完整，動態自然。";
            }
            else
            {
                if (prompt.Length > 0 && !prompt.EndsWith(".")) prompt += ".";
                prompt += " ";
                if (info.Artist.Length > 0 || info.Title.Length > 0)
                {
                    prompt += "Style reference: " + info.Artist;
                    if (info.Title.Length > 0) prompt += " - " + info.Title;
                    prompt += ". Original composition, not a cover and not a melody copy. ";
                }
                if (instrumental)
                    prompt += "Instrumental only. Absolutely no vocals, no singing, no rap, no spoken words, no choir, no humming, no vocal chops. ";
                prompt += "High-quality production, cohesive arrangement, natural dynamics.";
            }
            suggestion.Prompt = prompt;
            string negative = (chinese ? GetString(best, "negative_zh") : GetString(best, "negative")).Trim().TrimEnd(',');
            string lowerNegative = negative.ToLowerInvariant();
            if (chinese)
            {
                if (!negative.Contains("低品質")) negative += "，低品質、爆音、混音混濁";
                if (instrumental && !negative.Contains("人聲")) negative += "、人聲、歌唱、饒舌、朗讀";
            }
            else
            {
                if (!lowerNegative.Contains("low quality")) negative += ", low quality, clipping, muddy mix";
                if (instrumental && !lowerNegative.Contains("vocal")) negative += ", vocals, singing, rap, spoken words";
            }
            suggestion.Negative = negative.TrimStart(',', ' ');
            return suggestion;
        }

        private static List<Dictionary<string, object>> LoadGenres()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "genres.json");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    List<Dictionary<string, object>> parsed =
                        Serializer.Deserialize<List<Dictionary<string, object>>>(
                            System.IO.File.ReadAllText(path, Encoding.UTF8));
                    if (parsed != null && parsed.Count > 0) return parsed;
                }
                catch (Exception)
                {
                }
            }
            return new List<Dictionary<string, object>>();
        }

        private static string Fetch(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            request.Accept = "application/json,text/html,*/*";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static Dictionary<string, object> ParseJson(string json)
        {
            Dictionary<string, object> data =
                Serializer.Deserialize<Dictionary<string, object>>(json);
            if (data == null) throw new InvalidOperationException("無法解析 API 回應。");
            return data;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }

        private static string CleanTitle(string title)
        {
            string cleaned = Regex.Replace(title,
                @"\s*[\(\[](official|lyrics?|audio|video|visuali[sz]er|remaster(ed)?|4k|hd|mv)[^\)\]]*[\)\]]",
                "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned.Length > 0 ? cleaned : title.Trim();
        }
    }
}
