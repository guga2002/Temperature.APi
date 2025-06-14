using Common.BotNatia.Interfaces;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using Dapper;
using Microsoft.VisualBasic;
using NAudio.Wave;
using System.Data;
using System.Diagnostics;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace Common.BotNatia.Services;

public class ChanellServices: IChanellServices
{
    private readonly HttpClient _httpClient;
    private readonly IDbConnection _db;


    public ChanellServices(HttpClient httpClient, IDbConnection db)
    {
        _httpClient = httpClient;
        _db= db;
    }

    public async Task<List<int>> GetPortsWhereAlarmsIsOn()
    {
        List<int> lst = new List<int>();
        try
        {
            var rand=new Random();
            string link = $"http://192.168.20.250/goform/formEMR30?type=8&cmd=1&language=0&slotNo=255&alarmSlotNo=NaN&ran=0.3621{rand.Next()}";

            var res = await _httpClient.GetAsync(link);

            if (res.IsSuccessStatusCode)
            {
                var re = await res.Content.ReadAsStringAsync();

                var splitResult = re.Split(new string[] { "<*1*>", "<html>", "<html/>", "</html>" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in splitResult.OrderByDescending(io => io.Contains("Main GbE Card (C451E)")))
                {
                    if (item.Contains("Card 7 GbE"))
                    {
                        if (!string.IsNullOrEmpty(item))
                        {
                            var axali = item.Split(new string[] { "Card (C451E): Card 7" }, StringSplitOptions.None);
                            if (axali.Length >= 2)
                            {
                                string pattern = @"Port (\d+)";
                                Match match = Regex.Match(axali[1], pattern);
                                if (match.Success)
                                {
                                    string portNumber = match.Groups[1].Value;


                                    if (!lst.Any(io => io.ToString() == portNumber))
                                    {

                                        lst.Add(int.Parse(portNumber));
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("portis nomeri ver vnaxet");
                                }

                            }

                        }

                    }

                }

            }
            return lst.OrderBy(io => io).ToList();

        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync(ex.Message);
            return null;
        }
    }

    public async Task<List<string>> GetChannelsByPortIn250ListAsync(List<int> portIds)
    {
        const string sql = @"select Name_Of_Chanell from Chanells ch where ch.Port_In_250 IN @ids";

        var result = await _db.QueryAsync<string>(sql, new { ids = portIds });
        return result.AsList();
    }

    public async Task<byte[]> SpeakNow(string text, int baseRate = 2)
    {
        try
        {
            Random random = new Random();
            await Task.Delay(1);

            using (var synthesizer = new SpeechSynthesizer())
            using (var memoryStream = new MemoryStream())
            {
                var voices = synthesizer.GetInstalledVoices()
                                         .Where(voice => voice.Enabled)
                                         .ToList();

                var selectedVoice = voices.FirstOrDefault(voice =>
                    voice.VoiceInfo.Culture.Name.StartsWith("ka-GE") &&
                    voice.VoiceInfo.Name.Contains("Nati"));

                if (selectedVoice != null)
                {
                    synthesizer.SelectVoice(selectedVoice.VoiceInfo.Name);

                    synthesizer.Rate = baseRate + random.Next(-2, 3);
                    synthesizer.Volume = random.Next(75, 101);

                    synthesizer.SetOutputToWaveStream(memoryStream);

                    synthesizer.Speak(text);

                    return memoryStream.ToArray();
                }
                else
                {
                    Console.WriteLine("No matching voice found.");
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while speaking: {ex.Message}");
            return null;
        }
    }


    public async Task<Stream> ConvertWavToOggStreamAsync(byte[] wavData)
    {
        var ffmpegPath = @"C:\Tools\ffmpeg-master-latest-win64-gpl-shared\bin\ffmpeg.exe";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-i pipe:0 -c:a libopus -b:a 64k -f ogg pipe:1",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        await process.StandardInput.BaseStream.WriteAsync(wavData);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();

        var oggStream = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(oggStream);
        oggStream.Position = 0;

        string errorLog = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"FFmpeg failed. Error: {errorLog}");
        }

        return oggStream;
    }
}
