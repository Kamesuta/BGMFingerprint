using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.Emy;
using SoundFingerprinting.Extensions.LMDB;
using System.Text.RegularExpressions;

namespace BGMFingerprint // Note: actual namespace depends on the project name.
{
    public static class StringExt
    {
        public static string Truncate(this string value, int maxLength, string truncationSuffix = "…")
        {
            return value.Length > maxLength
                ? value[..maxLength] + truncationSuffix
                : value;
        }
    }

    internal class Program : IDisposable
    {
        private readonly LMDBModelService ModelService = new LMDBModelService("db");
        private readonly IAudioService AudioService = new FFmpegAudioService();

        public void Dispose()
        {
            ModelService.Dispose();
        }

        private void CancelAndExit()
        {
            Dispose();
            Console.WriteLine("終了");
            Environment.Exit(1);
        }

        static async Task Main(string[] args)
        {
            using var program = new Program();
            Console.CancelKeyPress += (sender, args) => program.CancelAndExit();

            if (args.Length < 2)
            {
                Console.WriteLine("Specify directory for inserting data");
                return;
            }
            var mode = args[0];
            switch (mode)
            {
                case "fingerprint":
                    await program.Fingerprint(args[1]);
                    break;

                case "query":
                    await program.Query(args[1]);
                    break;
            }
        }

        async Task Fingerprint(string pathToDirectory)
        {
            string[] files = Directory.GetFiles(pathToDirectory);
            var regex = new Regex(@"\[([_\-A-Za-z0-9]+?)\]$", RegexOptions.Compiled);

            int numInvalid = 0;
            foreach (string file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var m = regex.Match(name);
                if (!m.Success)
                {
                    Console.WriteLine($"不正な名前のファイル {name}");
                    ++numInvalid;
                }
            }
            if (numInvalid > 0)
            {
                return;
            }

            int numCount = -1;
            var errors = new List<string>();
            foreach (string file in files)
            {
                ++numCount;
                var name = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var m = regex.Match(name);
                    var matchId = m.Groups[1].Value;

                    var id = $"dova-s/{matchId}";

                    if (ModelService.ReadTrackById(id) != null)
                    {
                        Console.WriteLine($"[{numCount}/{files.Length}] We've already inserted {id}. Skipping.");
                        continue;
                    }

                    var track = new TrackInfo(id, name.Truncate(100), string.Empty);
                    var hashes = await FingerprintCommandBuilder
                        .Instance
                        .BuildFingerprintCommand()
                        .From(file)
                        .UsingServices(AudioService)
                        .Hash();

                    ModelService.Insert(track, hashes);
                    Console.WriteLine($"[{numCount}/{files.Length}] Inserted {id} with {hashes.Count} fingerprints.");
                }
                catch (Exception ex)
                {
                    errors.Add($"filename:{name}, {ex.Message}");
                    Console.Error.WriteLine($"[{numCount}/{files.Length}] エラー filename {name}");
                }
            }

            foreach (var error in errors)
            {
                Console.Error.WriteLine(error);
            }
        }

        async Task Query(string pathToQueryFile)
        {
            //foreach (var a in ModelService.GetTrackIds())
            //{
            //    ModelService.DeleteTrack(a);
            //}
            var result = await QueryCommandBuilder.Instance
                .BuildQueryCommand()
                .From(pathToQueryFile)
                .UsingServices(ModelService, AudioService)
                .Query();

            if (result.ContainsMatches)
            {
                foreach (var (entry, _) in result.ResultEntries)
                {
                    if (entry?.Confidence > 0.3)
                    {
                        Console.WriteLine($"Found {entry.Track.Id} with coverage {entry.TrackRelativeCoverage}, Query match starts at: {entry.QueryMatchStartsAt:0.00}, Track match starts at: {entry.TrackMatchStartsAt:0.00}");

                        //ModelService.RegisterMatches(new[] { entry }, new Dictionary<string, string>());
                    }
                }
            }
        }
    }
}