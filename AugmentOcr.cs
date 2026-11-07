using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
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
        private static Process _worker;
        private static StreamWriter _workerInput;
        private static readonly ConcurrentQueue<string> WorkerLines = new ConcurrentQueue<string>();
        private static readonly object WorkerLock = new object();

        // Card title regions, calibrated against real CN augment screens.
        private static readonly int[,] CardRects1920 = new int[,]
        {
            { 300, 390, 310, 70 },
            { 700, 390, 360, 70 },
            { 1180, 390, 300, 70 }
        };

        // HexBox preset for 2560x1440; used when the game is 1440p.
        private static readonly int[,] CardRects2560 = new int[,]
        {
            { 615, 577, 347, 63 },
            { 1107, 577, 347, 63 },
            { 1598, 577, 347, 63 }
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

        public static void Shutdown()
        {
            lock (WorkerLock)
            {
                try
                {
                    if (_workerInput != null)
                    {
                        _workerInput.Close();
                    }
                }
                catch
                {
                }
                try
                {
                    if (_worker != null && !_worker.HasExited)
                    {
                        _worker.Kill();
                    }
                }
                catch
                {
                }
                _worker = null;
                _workerInput = null;
            }
        }

        private static bool EnsureWorker(string command)
        {
            lock (WorkerLock)
            {
                if (_worker != null && !_worker.HasExited)
                {
                    return true;
                }
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = command;
                    startInfo.Arguments = "worker";
                    startInfo.WorkingDirectory = Path.GetDirectoryName(command);
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.RedirectStandardInput = true;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.StandardOutputEncoding = Encoding.UTF8;
                    startInfo.StandardErrorEncoding = Encoding.UTF8;

                    _worker = new Process();
                    _worker.StartInfo = startInfo;
                    _worker.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            WorkerLines.Enqueue(e.Data);
                        }
                    };
                    _worker.Start();
                    _worker.BeginOutputReadLine();
                    _workerInput = _worker.StandardInput;
                    return true;
                }
                catch
                {
                    _worker = null;
                    _workerInput = null;
                    return false;
                }
            }
        }

        private static List<AugmentOcrLine> TryWorker(string command)
        {
            List<AugmentOcrLine> result = new List<AugmentOcrLine>();
            if (!EnsureWorker(command) || _workerInput == null)
            {
                return result;
            }

            string ignored;
            while (WorkerLines.TryDequeue(out ignored))
            {
            }

            try
            {
                _workerInput.WriteLine("{}");
                _workerInput.Flush();
            }
            catch
            {
                return result;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 1600)
            {
                string line;
                if (WorkerLines.TryDequeue(out line))
                {
                    result = ParseWorkerLine(line);
                    if (result.Count == 3)
                    {
                        return result;
                    }
                }
                Thread.Sleep(25);
            }
            return result;
        }

        private static List<AugmentOcrLine> ParseWorkerLine(string line)
        {
            List<AugmentOcrLine> result = new List<AugmentOcrLine>();
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(line) as Dictionary<string, object>;
                if (root == null)
                {
                    return result;
                }

                object payloadsValue;
                if (root.TryGetValue("payloads", out payloadsValue) && payloadsValue != null)
                {
                    object[] payloads = payloadsValue as object[];
                    if (payloads != null)
                    {
                        for (int i = 0; i < payloads.Length; i++)
                        {
                            result.Add(ReadLine(payloads[i] as Dictionary<string, object>));
                        }
                    }
                }

                if (result.Count == 0)
                {
                    object inferenceValue;
                    if (root.TryGetValue("ocr_inference", out inferenceValue) && inferenceValue != null)
                    {
                        Dictionary<string, object> inference = inferenceValue as Dictionary<string, object>;
                        object slotsValue;
                        if (inference != null && inference.TryGetValue("slots", out slotsValue) && slotsValue != null)
                        {
                            object[] slots = slotsValue as object[];
                            if (slots != null)
                            {
                                for (int i = 0; i < slots.Length; i++)
                                {
                                    Dictionary<string, object> slot = slots[i] as Dictionary<string, object>;
                                    if (slot == null)
                                    {
                                        continue;
                                    }
                                    AugmentOcrLine item = new AugmentOcrLine();
                                    object textValue;
                                    if (slot.TryGetValue("raw_text", out textValue) && textValue != null)
                                    {
                                        item.Text = Convert.ToString(textValue);
                                    }
                                    if (string.IsNullOrEmpty(item.Text) && slot.TryGetValue("match_name", out textValue) && textValue != null)
                                    {
                                        item.Text = Convert.ToString(textValue);
                                    }
                                    object scoreValue;
                                    if (slot.TryGetValue("confidence", out scoreValue) && scoreValue != null)
                                    {
                                        double parsedScore;
                                        if (double.TryParse(Convert.ToString(scoreValue), out parsedScore))
                                        {
                                            item.Score = parsedScore;
                                        }
                                    }
                                    result.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                return new List<AugmentOcrLine>();
            }
            return result;
        }

        public static List<AugmentOcrLine> Scan(Bitmap screen, Native.RECT game, string command)
        {
            List<AugmentOcrLine> result = new List<AugmentOcrLine>();
            if (screen == null || string.IsNullOrEmpty(command) || !File.Exists(command))
            {
                return result;
            }

            // Prefer the warm persistent worker; it avoids a ~1.2s cold start per scan.
            List<AugmentOcrLine> warm = TryWorker(command);
            int warmTexts = 0;
            for (int i = 0; i < warm.Count; i++)
            {
                if (!string.IsNullOrEmpty(warm[i].Text) && warm[i].Score > 0.0)
                {
                    warmTexts++;
                }
            }
            if (warm.Count == 3 && warmTexts >= 2)
            {
                return warm;
            }

            int gameWidth = game.Right - game.Left;
            int gameHeight = game.Bottom - game.Top;
            if (gameWidth <= 0 || gameHeight <= 0)
            {
                return result;
            }

            string tempDir = Path.Combine(AppConfig.ConfigDir, "ocr-tmp");
            Directory.CreateDirectory(tempDir);
            bool is1440p = gameWidth >= 2400;
            int[,] cardRects = is1440p ? CardRects2560 : CardRects1920;
            int baseWidth = is1440p ? 2560 : 1920;
            int baseHeight = is1440p ? 1440 : 1080;
            double sx = gameWidth / (double)baseWidth;
            double sy = gameHeight / (double)baseHeight;

            List<string> paths = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                int x = (int)Math.Round(cardRects[i, 0] * sx);
                int y = (int)Math.Round(cardRects[i, 1] * sy);
                int w = (int)Math.Round(cardRects[i, 2] * sx);
                int h = (int)Math.Round(cardRects[i, 3] * sy);
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
                paths.Add(path);
            }

            // One sidecar process for all three cards is much faster than three startups.
            List<AugmentOcrLine> batch = RunBatchCommand(command, paths);
            if (batch.Count == 3)
            {
                return batch;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                result.Add(RunCommand(command, paths[i]));
            }
            return result;
        }

        private static List<AugmentOcrLine> RunBatchCommand(string command, List<string> paths)
        {
            List<AugmentOcrLine> result = new List<AugmentOcrLine>();
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = command;
                string arguments = "service --images";
                for (int i = 0; i < paths.Count; i++)
                {
                    arguments += " \"" + paths[i] + "\"";
                }
                startInfo.Arguments = arguments;
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
                        return result;
                    }
                    string stdout = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(8000))
                    {
                        try { process.Kill(); } catch { }
                        return result;
                    }

                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> root = serializer.DeserializeObject(stdout) as Dictionary<string, object>;
                    if (root == null)
                    {
                        return result;
                    }
                    object payloadsValue;
                    if (!root.TryGetValue("payloads", out payloadsValue) || payloadsValue == null)
                    {
                        return result;
                    }
                    object[] payloads = payloadsValue as object[];
                    if (payloads == null)
                    {
                        return result;
                    }
                    for (int i = 0; i < payloads.Length; i++)
                    {
                        Dictionary<string, object> payload = payloads[i] as Dictionary<string, object>;
                        result.Add(ReadLine(payload));
                    }
                }
            }
            catch
            {
                return new List<AugmentOcrLine>();
            }
            return result;
        }

        private static AugmentOcrLine ReadLine(Dictionary<string, object> payload)
        {
            AugmentOcrLine line = new AugmentOcrLine();
            if (payload == null)
            {
                return line;
            }
            object linesValue;
            if (!payload.TryGetValue("lines", out linesValue) || linesValue == null)
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
            return line;
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
