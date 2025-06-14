using Common.BotNatia.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Common.BotNatia.Job
{
    public class StreamAnalytics : BackgroundService
    {

        private static int _lastTotalMessageCount = -1;
        private readonly IServiceProvider _serviceProvider;

        public StreamAnalytics(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Console.Out.WriteLineAsync("daiwyoo");
            var scope = _serviceProvider.CreateScope();

            var redis = scope.ServiceProvider.GetRequiredService<IMemoryCache>();

            var multicastStreams = new List<(string ip, int port)>
        {
            ("224.200.200.200", 10071),
            ("224.200.200.200", 10072),
            ("224.200.200.200", 10073),
            ("224.200.200.200", 10074),
            ("224.200.200.200", 10075),
            ("224.200.200.200", 10076),
            ("224.200.200.200", 10077),
        };

            var allMessages = new List<string>();

            var unite = new List<MulticastAnalysisResult>();
            MulticastAnalysisResult result = new MulticastAnalysisResult();
            foreach (var (ip, port) in multicastStreams)
            {
                var analyzer = new MulticastProgramAnalyzer(ip, port, durationSeconds: 10);
                result = await analyzer.RunAsync();

                if (result.ProblematicProgramIds?.Count == 0)
                    continue;
                result.Ip = $"{ip}:{port}";
                var ttsMessages = GenerateTtsFriendlyMessagesInGeorgian(ip, port, result);
                allMessages.AddRange(ttsMessages);
                unite.Add(result);
            }

            var res = unite.Select(i => new MulticastAnalysisResult
            {
                DurationSeconds = i.DurationSeconds,
                BitrateKbps = i.BitrateKbps,
                ProblematicProgramIds = i.ProblematicProgramIds,
                Programs = i.Programs?.ToList(),
                TotalPackets = i.TotalPackets,
                Ip = i.Ip,
            });

             redis.Set("SystemStreamInfo", res);

            if (_lastTotalMessageCount == allMessages.Count)
                return;

            _lastTotalMessageCount = allMessages.Count;

             redis.Set("letscheck", allMessages.Where(i => !i.Contains("არ შეიცავს ვიდეო ნაკადს")).ToList(), TimeSpan.FromMinutes(30));
        }


        static List<string> GenerateTtsFriendlyMessagesInGeorgian(string ip, int port, MulticastAnalysisResult result)
        {
            var messages = new List<string>();

            foreach (var program in result?.Programs ?? new List<ProgramAnalysisResult>())
            {
                if (!program.IsProblematic)
                    continue;

                var msg = $"პროგრამა {program.ProgramId} ({ip}:{port})";

                if (!program.HasVideo)
                    msg += " არ შეიცავს ვიდეო ნაკადს";

                if (program.MissingStreams?.Count > 0)
                {
                    var types = string.Join(" და ", program.MissingStreams.ConvertAll(m => m.Type switch
                    {
                        "MPEG-1 Audio" => "მპეგ-1 აუდიო",
                        "MPEG-2 Audio" => "მპეგ-2 აუდიო",
                        "AAC Audio" => "ეიეისი აუდიო",
                        "Subtitles" => "სუბტიტრები",
                        _ => m.Type?.ToLower()
                    }));

                    msg += program.HasVideo ? $", აკლია {types}" : $", და აკლია {types}";
                }

                foreach (var stream in program?.Streams ?? new List<StreamInfo>())
                {
                    if (stream.ContinuityErrors > 10)
                        msg += $", PID {stream.Pid} ({stream.Type}) შეიცავს {stream.ContinuityErrors} შეცდომას";
                }

                msg += ".";
                messages.Add(msg);
            }

            return messages;
        }
    }
}
