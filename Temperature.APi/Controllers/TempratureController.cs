using System.Net.Mail;
using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace Temperature.APi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TempratureController : ControllerBase
{

    private const string SmtpServer = "smtp.gmail.com";
    private const int SmtpPort = 587;
    private const string SenderEmail = "globaltvmanagment@gmail.com";
    private const string SenderPassword = "reoiuyqgeipepngo";
    private readonly HttpClient _client;
    private const string serverUrl = "http://192.168.100.104:8080";

    public TempratureController(HttpClient client)
    {
        _client = client;
    }

    [HttpGet("GetCurrentTemperature")]
    public async Task<IActionResult> GetCurrentTemperature()
    {
        try
        {
            var response = await _client.GetAsync($"{serverUrl}/monitoring");
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadAsStringAsync();
            var res = responseData.Split(new string[] { "<div id=\"temperature\" class=\"temperature\">", "°C</div>" }, StringSplitOptions.None)[1];
            var Humidity = responseData.Split(new string[] { "<div class=\"humidity\">", "%</div>" }, StringSplitOptions.None)[1];
            return Ok(
                new TemperatureResponse
                {
                    Temperature = res.Trim(),
                    Humidity = Humidity.Trim()
                });
        }
        catch (Exception ex)
        {
            return Ok(
                new TemperatureResponse
                {
                    Temperature = "0.0",
                    Humidity = "0.0",
                });
        }
    }

    [HttpPost("SendEmail")]
    public async Task SendMessage(string message, List<string> tos)
    {
        try
        {
            using var smtpClient = new SmtpClient(SmtpServer)
            {
                Port = SmtpPort,
                Credentials = new NetworkCredential(SenderEmail, SenderPassword),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(SenderEmail),
                Subject = $"საიტი გაითშა გუგა: {DateTime.Now}",
                Body = message,
                IsBodyHtml = true
            };

            foreach (var to in tos)
            {
                mailMessage.To.Add(to);
            }

            await smtpClient.SendMailAsync(mailMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send email: {ex.Message}");
        }
    }


    public class TemperatureResponse
    {
        public string? Temperature { get; set; }
        public string? Humidity { get; set; }
    }
}
