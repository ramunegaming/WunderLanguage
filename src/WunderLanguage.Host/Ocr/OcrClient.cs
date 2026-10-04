using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace WunderLanguage.Host.Ocr
{
    public record OcrBlock(string Text, int X, int Y, int Width, int Height, double Area);

    public class OcrClient
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        // S1.2: last-loop timings retained for the min-spec verdict (elsewhere log-only).
        public double LastDetMs { get; private set; }
        public double LastRecMs { get; private set; }

        public OcrClient(string endpoint = "http://localhost:8000")
        {
            _endpoint = endpoint;
            _http = new HttpClient();
        }

        public async Task<string> ExtractTextAsync(Bitmap bitmap)
        {
            var (text, _, _, _) = await ExtractTextWithBlocksAsync(bitmap);
            return text;
        }

        public async Task<(string Text, List<OcrBlock> Blocks, int ContentWords, int TotalWords)> ExtractTextWithBlocksAsync(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;

            using var content = new MultipartFormDataContent();
            using var imageContent = new StreamContent(ms);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "file", "capture.png");

            var response = await _http.PostAsync($"{_endpoint}/ocr", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string text = root.TryGetProperty("text", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;

            var blocks = new List<OcrBlock>();
            if (root.TryGetProperty("blocks", out var b) && b.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in b.EnumerateArray())
                {
                    try
                    {
                        string bt = item.TryGetProperty("text", out var btx) ? (btx.GetString() ?? "") : "";
                        if (string.IsNullOrWhiteSpace(bt))
                            continue;
                        int x = 0, y = 0, w = 0, h = 0;
                        if (item.TryGetProperty("box", out var box) && box.ValueKind == JsonValueKind.Array && box.GetArrayLength() >= 4)
                        {
                            x = box[0].GetInt32(); y = box[1].GetInt32();
                            w = box[2].GetInt32(); h = box[3].GetInt32();
                        }
                        double area = 0;
                        if (item.TryGetProperty("area", out var ar) && ar.ValueKind == JsonValueKind.Number && ar.TryGetDouble(out var ad))
                            area = ad;
                        else
                            area = (double)w * h;
                        blocks.Add(new OcrBlock(bt, x, y, w, h, area));
                    }
                    catch { }
                }
            }

            int contentWords = 0, totalWords = 0;
            try
            {
                if (root.TryGetProperty("words", out var w) && w.ValueKind == JsonValueKind.Object)
                {
                    if (w.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var ci)) contentWords = ci;
                    if (w.TryGetProperty("total", out var t2) && t2.ValueKind == JsonValueKind.Number && t2.TryGetInt32(out var ti)) totalWords = ti;
                }
            }
            catch { }
            try
            {
                if (root.TryGetProperty("timing", out var tm) && tm.ValueKind == JsonValueKind.Object)
                {
                    if (Settings.Diag.Enabled) FileLogger.Log($"[ocr-timing] {tm}");
                    if (tm.TryGetProperty("det_ms", out var dm) && dm.TryGetDouble(out var dd)) LastDetMs = dd;
                    if (tm.TryGetProperty("rec_ms", out var rm) && rm.TryGetDouble(out var rd)) LastRecMs = rd;
                }
            }
            catch { }
            return (text, blocks, contentWords, totalWords);
        }
    }
}