namespace Common.BotNatia.Interfaces;

public interface IChanellServices
{
    Task<List<int>> GetPortsWhereAlarmsIsOn();
    Task<List<string>> GetChannelsByPortIn250ListAsync(List<int> portIds);
}
