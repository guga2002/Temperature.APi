using Common.BotNatia.MesageSender;
using Common.BotNatia.Job;
using Common.BotNatia.Interfaces;
using Common.BotNatia.Services;
using System.Data;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<MulticastProgramAnalyzer>();
builder.Services.AddSingleton<BootSendInfo>();
builder.Services.AddScoped<IChanellServices,ChanellServices>();
builder.Services.AddHostedService<ChanellChecker>();
builder.Services.AddHostedService<MentionResponderService>();
builder.Services.AddHostedService<StreamAnalytics>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();


builder.Services.AddScoped<IDbConnection>(provider =>
{
    var connectionString = "Server=192.168.1.102 ;Database=JandagBase;User Id=Guga13guga ;Password= Guga13gagno!;Encrypt=True;TrustServerCertificate=True;";
    var connection = new SqlConnection(connectionString);
    connection.Open();
    return connection;
});

builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(2000, listenOptions =>
    {
        listenOptions.UseHttps();
    });
});
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
