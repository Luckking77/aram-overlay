using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AramOverlay
{
    public class AugmentOcrLine
    {
        public string Text { get; set; }
        public double Score { get; set; }

        public AugmentOcrLine()
        {
            Text = "";
        }
    }

    public static class AugmentOcr
    {
        // 1920x1080 preset from HexBox OCR presets (see OCR-SOURCES.md).
        private static readonly int[,] CardRects = new int[,]
        {
            { 462, 433, 260, 47 },
            { 830, 433, 260, 47 },
            { 1199, 433, 260, 47 }
        };

        public static string FindCommand()
        {
            string env = Environment.GetEnvironmentVariable("ARAM_OVERLAY_OCR_COMMAND");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
            {
                return env;
            }

            string[] candidates = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr", "hexbox-ocr.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hexbox-ocr.exe"),
                @"C:\Users\50353\Desktop\dsh\_work\hexbox\ocr\hexbox-ocr.exe"
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return "";
        }

        public static List<AugmentOcrLine> Scan(Bitmap screen, Native.RECT game, string command)
        {
            List<AugmentOcrLine> result = new List<AugmentOcrLine>();
            if (screen == null || string.IsNullOrEmpty(command) || !File.Exists(command))
            {
                return result;
            }

            int gameWidth = game.Right - game.Left;
            int gameHeight = game.Bottom - game.Top;
            if (gameWidth <= 0 || gameHeight <= 0)
            {
                return result;
            }

            string tempDir = Path.Combine(AppConfig.ConfigDir, "ocr-tmp");
            Directory.CreateDirectory(tempDir);
            double sx = gameWidth / 1920.0;
            double sy = gameHeight / 1080.0;

            for (int i = 0; i < 3; i++)
            {
                int x = (int)Math.Round(CardRects[i, 0] * sx);
                int y = (int)Math.Round(CardRects[i, 1] * sy);
                int w = (int)Math.Round(CardRects[i, 2] * sx);
                int h = (int)Math.Round(CardRects[i, 3] * sy);
                x = Math.Max(0, Math.Min(screen.Width - 2, x));
                y = Math.Max(0, Math.Min(screen.Height - 2, y));
                w = Math.Max(2, Math.Min(screen.Width - x, w));
                h = Math.Max(2, Math.Min(screen.Height - y, h));

                string path = Path.Combine(tempDir, "card-" + i.ToString() + ".png");
                using (Bitmap crop = new Bitmap(w, h))
                using (Graphics graphics = Graphics.FromImage(crop))
                {
                    graphics.DrawImage(screen, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                    crop.Save(path, ImageFormat.Png);
                }

                AugmentOcrLine line = RunCommand(command, path);
                result.Add(line);
            }
            return result;
        }

        private static AugmentOcrLine RunCommand(string command, string imagePath)
        {
            AugmentOcrLine line = new AugmentOcrLine();
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = command;
                startInfo.Arguments = "service --image \"" + imagePath + "\"";
                startInfo.WorkingDirectory = Path.GetDirectoryName(command);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return line;
                    }
                    string stdout = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(8000))
                    {
                        try { process.Kill(); } catch { }
                        return line;
                    }

                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> root = serializer.DeserializeObject(stdout) as Dictionary<string, object>;
                    if (root == null)
                    {
                        return line;
                    }
                    object linesValue;
                    if (!root.TryGetValue("lines", out linesValue) || linesValue == null)
                    {
                        return line;
                    }
                    object[] lines = linesValue as object[];
                    if (lines == null)
                    {
                        return line;
                    }
                    for (int i = 0; i < lines.Length; i++)
                    {
                        Dictionary<string, object> item = lines[i] as Dictionary<string, object>;
                        if (item == null)
                        {
                            continue;
                        }
                        string text = "";
                        double score = 0;
                        object textValue;
                        object scoreValue;
                        if (item.TryGetValue("text", out textValue) && textValue != null)
                        {
                            text = Convert.ToString(textValue);
                        }
                        if (item.TryGetValue("score", out scoreValue) && scoreValue != null)
                        {
                            double.TryParse(Convert.ToString(scoreValue), out score);
                        }
                        if (text.Length > 0 && score >= line.Score)
                        {
                            line.Text = text;
                            line.Score = score;
                        }
                    }
                }
            }
            catch
            {
                return new AugmentOcrLine();
            }
            return line;
        }
    }
}
