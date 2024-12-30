using System.Diagnostics;

namespace RTSPPlugin
{
    /// <summary>
    /// Merge all selected video path into one single video, the videos needs to be in the 
    /// </summary>
    public class VideoMerge
    {
        /// <summary>
        /// Stores all process running by VideoMerger
        /// </summary>
        public static readonly List<int> ActivesMergers = [];

        /// <summary>
        /// Kill all process running in ActivesMergers
        /// </summary>
        public static void KillActivesMergers()
        {
            ActivesMergers.ForEach((processId) =>
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (process.ProcessName == "ffmpeg")
                    {
                        Debug.WriteLine($"Video Converter has been killed with id: {processId}");
                        process.Kill();
                    };
                }
                catch (Exception) { }
            });
        }

        private readonly string Arguments = string.Empty;
        private readonly string FfmpegPath = string.Empty;
        private readonly string OutputPath = string.Empty;
        private readonly string OutputDirectory = string.Empty;
        private Process? FfmpegProcess = null;

        /// <summary>
        /// If any errors occurs will be stored in this variable
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Returns the error message
        /// </summary>
        public Action<string>? OnMergeFail { get; set; }

        /// <summary>
        /// Returns the output path from the stream, only invoked when the stream ends with the code 0
        /// </summary>
        public Action<string>? OnMergeEnd { get; set; }

        public VideoMerge(
            string outputPath,
            string[] files,
            string? ffmpegPath = null,
            string resolution = "1920:1080",
            bool enableDebug = false)
        {
            if (ffmpegPath == null)
                FfmpegPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ffmpeg", "bin", "ffmpeg.exe");
            else
                FfmpegPath = ffmpegPath;

            OutputPath = outputPath;
            OutputDirectory = Path.GetDirectoryName(OutputPath) ??
                throw new ArgumentException($"Cannot get the output directory from: {OutputPath}");

            if (!Directory.Exists(OutputDirectory))
                Directory.CreateDirectory(OutputDirectory);

            if (File.Exists(OutputPath))
                File.Delete(OutputPath);

            if (enableDebug)
                Console.WriteLine($"[VideoMerge] Starting...");

            string concatFiles = string.Join("|", files.Select(file => file.Replace(@"\", "/")));
            Arguments = $"-i \"concat:{concatFiles}\" -vf scale={resolution} \"{outputPath}\"";

            if (enableDebug)
                Console.WriteLine($"[VideoMerge Arguments] {Arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = Arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            FfmpegProcess = new()
            {
                StartInfo = startInfo
            };

            FfmpegProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if (enableDebug)
                        Console.WriteLine($"[VideoMerge Output] {e.Data}");
                }

            };

            FfmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if (enableDebug)
                        Console.WriteLine($"[VideoMerge Error] {e.Data}");
                }
            };

            FfmpegProcess.EnableRaisingEvents = true;
            FfmpegProcess.Exited += (sender, e) =>
            {
                if (FfmpegProcess?.ExitCode == 0)
                    OnMergeEnd?.Invoke(OutputPath);
            };

            FfmpegProcess.Start();
            FfmpegProcess.BeginOutputReadLine();
            FfmpegProcess.BeginErrorReadLine();

            ActivesMergers.Add(FfmpegProcess.Id);
            if (enableDebug)
                Console.WriteLine($"[VideoMerge] Starting Ffmpeg Process");
        }

        /// <summary>
        /// Stops all the stream process and clean the memory
        /// </summary>
        /// <returns></returns>
        public Task Dispose()
        {
            int? processId = FfmpegProcess?.Id;
            try
            {
                // Safe close
                FfmpegProcess?.StandardInput.WriteLine("q");
            }
            catch (Exception) { }

            try
            {
                // Memory cleanup
                ActivesMergers.Remove((int)processId!);
                FfmpegProcess?.Close();
                FfmpegProcess?.Dispose();
            }
            catch (Exception) { }
            FfmpegProcess = null;

            // Wait until process is finished
            return Task.Run(async () =>
            {
                if (processId == null) return;

                int maxTries = 50;
                int actualTries = 0;
                while (true)
                {
                    try
                    {
                        var processExistance = Process.GetProcessById((int)processId);
                        if (processExistance.ProcessName != "ffmpeg") return;

                        if (actualTries >= maxTries)
                        {
                            ActivesMergers.Remove((int)processId!);
                            processExistance.Kill();
                            return;
                        };

                        await Task.Delay(50);
                        actualTries++;
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            });
        }
    }
}
