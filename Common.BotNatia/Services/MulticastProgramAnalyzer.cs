using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net;

namespace Common.BotNatia.Services;

public class MulticastProgramAnalyzer
{
    private readonly string _ip;
    private readonly int _port;
    private readonly int _durationSeconds;

    private long _totalBytes = 0;
    private int _totalPackets = 0;

    private readonly ConcurrentDictionary<int, int> _pidCounts = new();
    private readonly ConcurrentDictionary<int, int> _continuityErrors = new();
    private readonly ConcurrentDictionary<int, int> _lastContinuityCounter = new();
    private readonly ConcurrentDictionary<int, List<string>> _continuityErrorDetails = new();
    private readonly ConcurrentDictionary<int, int> _unknownPids = new();
    private readonly Dictionary<int, int> _pidFirstSeenPacket = new();
    private readonly Dictionary<int, int> _pidLastSeenPacket = new();

    private readonly Dictionary<ushort, ushort> _programToPmtPid = new();
    private readonly Dictionary<ushort, List<(string Type, ushort Pid)>> _programStreams = new();
    private readonly List<ushort> _problematicPrograms = new();

    public MulticastProgramAnalyzer(string ip, int port, int durationSeconds = 10)
    {
        _ip = ip;
        _port = port;
        _durationSeconds = durationSeconds;
    }

    public async Task<MulticastAnalysisResult> RunAsync()
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
        udp.JoinMulticastGroup(IPAddress.Parse(_ip));

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_durationSeconds));

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                _totalBytes += result.Buffer.Length;
                _totalPackets++;
                ParsePackets(result.Buffer);
            }
        }
        catch (OperationCanceledException) { }

        return GenerateAnalysis();
    }

    private void ParsePackets(byte[] data)
    {
        for (int i = 0; i + 188 <= data.Length; i += 188)
        {
            if (data[i] != 0x47) continue;

            int pid = ((data[i + 1] & 0x1F) << 8) | data[i + 2];
            int cc = data[i + 3] & 0x0F;

            _pidCounts.AddOrUpdate(pid, 1, (_, val) => val + 1);
            _pidLastSeenPacket[pid] = _totalPackets;
            if (!_pidFirstSeenPacket.ContainsKey(pid)) _pidFirstSeenPacket[pid] = _totalPackets;

            if (_lastContinuityCounter.TryGetValue(pid, out var lastCc))
            {
                int expected = (lastCc + 1) % 16;
                if (cc != expected)
                {
                    _continuityErrors.AddOrUpdate(pid, 1, (_, val) => val + 1);
                    _continuityErrorDetails.AddOrUpdate(pid,
                        _ => new List<string> { $"Expected {expected}, got {cc}" },
                        (_, list) => { if (list.Count < 5) list.Add($"Expected {expected}, got {cc}"); return list; });
                }
            }

            _lastContinuityCounter[pid] = cc;

            if ((data[i + 1] & 0x40) == 0x40)
            {
                int pointerField = data[i + 4];
                int payloadIndex = 5 + pointerField;
                if (payloadIndex + 3 >= 188) continue;

                var payload = data.AsSpan(i + payloadIndex, 188 - payloadIndex);
                if (pid == 0 && payload[0] == 0x00) ParsePAT(payload);
                else if (_programToPmtPid.Values.Contains((ushort)pid) && payload[0] == 0x02) ParsePMT(payload);
                else _unknownPids.AddOrUpdate(pid, 1, (_, v) => v + 1);
            }
        }
    }

    private void ParsePAT(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5) return;
        int sectionLength = ((data[1] & 0x0F) << 8) | data[2];
        int tableEnd = 3 + sectionLength - 4;

        for (int i = 8; i + 4 <= tableEnd; i += 4)
        {
            ushort programNumber = (ushort)((data[i] << 8) | data[i + 1]);
            ushort pmtPid = (ushort)(((data[i + 2] & 0x1F) << 8) | data[i + 3]);
            if (programNumber > 0)
                _programToPmtPid[programNumber] = pmtPid;
        }
    }

    private void ParsePMT(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5) return;
        int sectionLength = ((data[1] & 0x0F) << 8) | data[2];
        int tableEnd = 3 + sectionLength - 4;
        ushort programNumber = (ushort)((data[3] << 8) | data[4]);
        int programInfoLength = ((data[10] & 0x0F) << 8) | data[11];
        int pos = 12 + programInfoLength;

        if (!_programStreams.ContainsKey(programNumber))
            _programStreams[programNumber] = new();

        while (pos + 5 <= tableEnd)
        {
            byte streamType = data[pos];
            ushort pid = (ushort)(((data[pos + 1] & 0x1F) << 8) | data[pos + 2]);
            int esInfoLength = ((data[pos + 3] & 0x0F) << 8) | data[pos + 4];

            string typeStr = streamType switch
            {
                0x01 => "MPEG-1 Video",
                0x02 => "MPEG-2 Video",
                0x1B => "H.264 Video",
                0x24 => "H.265 Video",
                0x03 => "MPEG-1 Audio",
                0x04 => "MPEG-2 Audio",
                0x0F => "AAC Audio",
                0x06 => "Subtitles",
                _ => $"Unknown (0x{streamType:X2})"
            };

            if (!_programStreams[programNumber].Any(x => x.Pid == pid))
                _programStreams[programNumber].Add((typeStr, pid));

            pos += 5 + esInfoLength;
        }
    }

    private MulticastAnalysisResult GenerateAnalysis()
    {
        var result = new MulticastAnalysisResult
        {
            DurationSeconds = _durationSeconds,
            TotalPackets = _totalPackets,
            BitrateKbps = _totalBytes * 8.0 / _durationSeconds / 1000.0,
            Programs = new List<ProgramAnalysisResult>()
        };

        foreach (var (programId, streams) in _programStreams)
        {
            var programResult = new ProgramAnalysisResult
            {
                ProgramId = programId,
                Streams = new List<StreamInfo>()
            };

            bool hasVideo = false;
            foreach (var (type, pid) in streams)
            {
                _pidCounts.TryGetValue(pid, out int count);
                _continuityErrors.TryGetValue(pid, out int errors);
                var streamInfo = new StreamInfo { Type = type, Pid = pid, PacketCount = count, ContinuityErrors = errors };

                if (_continuityErrorDetails.TryGetValue(pid, out var details))
                    streamInfo.ErrorDetails = details;

                if (type.Contains("Video") && count > 0) hasVideo = true;
                programResult.Streams.Add(streamInfo);
            }

            var missing = streams.Where(s => !_pidCounts.ContainsKey(s.Pid)).ToList();
            programResult.MissingStreams = missing.Select(m => new StreamInfo { Type = m.Type, Pid = m.Pid }).ToList();
            programResult.HasVideo = hasVideo;

            programResult.IsProblematic = false;

            if (!hasVideo)
            {
                continue;
            }

            foreach (var s in programResult.Streams)
            {
                if (s.PacketCount > 0)
                {
                    double errorRate = (double)s.ContinuityErrors / s.PacketCount;
                    if (errorRate > 0.05)
                    {
                        programResult.IsProblematic = true;
                        s.ErrorDetails.Add($"High continuity error rate: {errorRate:P}");
                    }
                }
            }

            if (programResult.Streams.Any(s => s.PacketCount < 100))
                programResult.IsProblematic = true;

            if (programResult.MissingStreams.Any())
                programResult.IsProblematic = true;

            if (!_programStreams.ContainsKey(programId))
            {
                programResult.IsProblematic = true;
                programResult.Streams.Add(new StreamInfo
                {
                    Type = "Missing PMT",
                    Pid = _programToPmtPid[programId],
                    PacketCount = 0,
                    ContinuityErrors = 0,
                    ErrorDetails = new List<string> { "No PMT received for this program" }
                });
            }

            var unknownPidMatches = _unknownPids.Keys.Intersect(programResult.Streams.Select(s => (int)s.Pid));
            foreach (var pid in unknownPidMatches)
            {
                programResult.IsProblematic = true;
                programResult.Streams.Add(new StreamInfo
                {
                    Type = "Unknown PID",
                    Pid = (ushort)pid,
                    PacketCount = _unknownPids[pid],
                    ErrorDetails = new List<string> { "PID active but not referenced in PMT" }
                });
            }

            if (programResult.IsProblematic)
                _problematicPrograms.Add(programId);

            result.Programs.Add(programResult);
        }

        var pidToProgramMap = new Dictionary<ushort, List<ushort>>();
        foreach (var (programId, streams) in _programStreams)
        {
            foreach (var (_, pid) in streams)
            {
                if (!pidToProgramMap.ContainsKey(pid))
                    pidToProgramMap[pid] = new();
                pidToProgramMap[pid].Add(programId);
            }
        }

        foreach (var (pid, programs) in pidToProgramMap)
        {
            if (programs.Count > 1)
            {
                foreach (var prog in result.Programs.Where(p => programs.Contains(p.ProgramId)))
                {
                    prog.IsProblematic = true;
                    prog.Streams.Add(new StreamInfo
                    {
                        Type = "Conflicting PID",
                        Pid = pid,
                        ErrorDetails = new List<string> { $"PID {pid} shared across programs: {string.Join(", ", programs)}" }
                    });
                }
            }
        }

        result.ProblematicProgramIds = _problematicPrograms;
        return result;
    }
}

public class MulticastAnalysisResult
{
    public string? Ip { get; set; }
    public int DurationSeconds { get; set; }
    public int TotalPackets { get; set; }
    public double BitrateKbps { get; set; }
    public List<ProgramAnalysisResult>? Programs { get; set; }
    public List<ushort>? ProblematicProgramIds { get; set; }
}

public class ProgramAnalysisResult
{
    public ushort ProgramId { get; set; }
    public List<StreamInfo>? Streams { get; set; }
    public List<StreamInfo>? MissingStreams { get; set; }
    public bool HasVideo { get; set; }
    public bool IsProblematic { get; set; }
}

public class StreamInfo
{
    public string? Type { get; set; }
    public ushort Pid { get; set; }
    public int PacketCount { get; set; }
    public int ContinuityErrors { get; set; }
    public List<string> ErrorDetails { get; set; } = new();
}
