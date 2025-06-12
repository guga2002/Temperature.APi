var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//
builder.Services.AddHttpClient();

// Configure Kestrel to listen on port 2000 with HTTPS
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
