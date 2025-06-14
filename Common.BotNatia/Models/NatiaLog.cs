namespace Common.BotNatia.Models;

public class NatiaLog
{
    public int Id { get; set; }
    public DateTime ActionDate { get; set; }
    public string? WhatNatiaSaid { get; set; }
    public bool IsError { get; set; }
    public bool IsCritical { get; set; }
    public string? WhatWasTopic { get; set; }
    public int Priority { get; set; }
    public string? ChannelName { get; set; }
    public string? Satellite { get; set; }
    public string? SuggestedSolution { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}
