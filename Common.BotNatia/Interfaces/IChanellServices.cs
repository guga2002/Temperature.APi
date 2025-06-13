namespace Common.BotNatia.Interfaces;

public interface IChanellServices
{
    Task<List<int>> GetPortsWhereAlarmsIsOn();
    Task<List<string>> GetChannelsByPortIn250ListAsync(List<int> portIds);
    Task<Stream> ConvertWavToOggStreamAsync(byte[] wavData);
    Task<byte[]> SpeakNow(string text, int baseRate = 2);
}
