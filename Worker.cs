using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Reflection;
using System.Net.Mail;
using System.Net;
using System.Text;

namespace DemoWorkerService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _Logger;
        private const string _Main_API_URL            = "https://cdn-api.co-vin.in/api/v2/appointment/sessions/calendarByDistrict?district_id=725&date=";        
        private static readonly string _LogsDirectory = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/Logs";
        private static int _Cycle_Interval            = 10000;
        private const string _Token                   = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX25hbWUiOiI4N2I1MmU5Yi0xOWY0LTQ2MTctYWNmZS1kZDcwYmRkNGQ5ZmMiLCJ1c2VyX2lkIjoiODdiNTJlOWItMTlmNC00NjE3LWFjZmUtZGQ3MGJkZDRkOWZjIiwidXNlcl90eXBlIjoiQkVORUZJQ0lBUlkiLCJtb2JpbGVfbnVtYmVyIjo5MTYzODc4MTMwLCJiZW5lZmljaWFyeV9yZWZlcmVuY2VfaWQiOjM2NzI1NjgwOTE4OTA3LCJzZWNyZXRfa2V5IjoiYjVjYWIxNjctNzk3Ny00ZGYxLTgwMjctYTYzYWExNDRmMDRlIiwidWEiOiJNb3ppbGxhLzUuMCAoWDExOyBVYnVudHU7IExpbnV4IHg4Nl82NDsgcnY6ODguMCkgR2Vja28vMjAxMDAxMDEgRmlyZWZveC84OC4wIiwiZGF0ZV9tb2RpZmllZCI6IjIwMjEtMDUtMjBUMTI6MTY6MTIuMTQyWiIsImlhdCI6MTYyMTUxMjk3MiwiZXhwIjoxNjIxNTEzODcyfQ.A4AbUZ0FRZrcIZmag3lmLWUtemkmQXeSxIc3E51idcY";
        private const string _If_None_Match           = "W/\"e-J931ZK8g9XxU9GMT5EhN7zUIrxg\"";
            
        public Worker(ILogger<Worker> logger)
        {
            _Logger = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            if(!Directory.Exists(_LogsDirectory))
                Directory.CreateDirectory(_LogsDirectory);

            _Logger.LogInformation($"Worker service started : {Thread.CurrentThread.Name}");
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _Logger.LogInformation($"Worker service stopped : {Thread.CurrentThread.Name}");
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _Logger.LogInformation("Worker started at: {time}", DateTimeOffset.Now);

            while (!stoppingToken.IsCancellationRequested)
            {
                var requiredCenters = new List<Center> { };

                try
                { 
                    using(HttpClient client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:88.0) Gecko/20100101 Firefox/88.0");
                        client.DefaultRequestHeaders.IfNoneMatch.ParseAdd(_If_None_Match);
                        
                        client.DefaultRequestHeaders.Referrer = new Uri(@"https://selfregistration.cowin.gov.in/");
                        client.DefaultRequestHeaders.Host     = "cdn-api.co-vin.in";

                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _Token);

                        var finalURL          = _Main_API_URL + DateTime.Now.ToString("dd-MM-yyyy");

                        var result            = await client.GetAsync(finalURL);
                        
                        var stringResponse    = await result.Content.ReadAsStringAsync();
                        
                        if(result.IsSuccessStatusCode)
                        {
                            var response = JsonConvert.DeserializeObject<Dictionary<string, List<Center>>>(stringResponse);                         

                            if(response != null && response.ContainsKey("centers"))
                            {
                                var centers = response["centers"];

                                requiredCenters = new List<Center> { };

                                foreach(Center center in centers)
                                {
                                    if(center.sessions.Any(x => x.min_age_limit >= 18 && x.min_age_limit <= 30 && x.available_capacity_dose1 > 1))
                                        requiredCenters.Add(center);
                                }

                                if(requiredCenters.Count > 0)
                                {
                                    _Logger.LogInformation($"Success. Found : {requiredCenters.Count}");

                                    await SendEmail(GetEmailBody(requiredCenters), $"Covid Vaccine - {string.Join("," , requiredCenters.Select(x => x.pincode).Distinct())}");
                                }
                            }                            
                        }
                        else                        
                        {
                            await WriteToFile("Error", stringResponse);
                            _Logger.LogError($"Not success , status code : {result.StatusCode}, reason : {result.ReasonPhrase} : {DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")}", DateTimeOffset.Now);
                        }
                    }
                }
                catch (System.Exception ex)
                {   
                    await WriteToFile("Error", JsonConvert.SerializeObject(ex));
                    _Logger.LogError($"Error: {DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")} : Message : {ex.Message}", DateTimeOffset.Now);
                }                   

                await Task.Delay(_Cycle_Interval, stoppingToken);
            }

            _Logger.LogInformation("Worker stopped at: {time}", DateTimeOffset.Now);
        }

        private string GetEmailBody(List<Center> requiredCenters)
        {
            StringBuilder body = new StringBuilder();
            int index = 1;

            foreach(Center center in requiredCenters)
            {
                StringBuilder currentCenterInfo = new StringBuilder();

                currentCenterInfo.Append($"{index++}. {center.name} - {center.pincode}");
                currentCenterInfo.Append(Environment.NewLine);

                foreach(Session session in center.sessions)
                {
                    if(session.available_capacity_dose1 > 0)
                    {
                        currentCenterInfo.Append($"{session.date} -> {session.available_capacity_dose1} slots");
                        currentCenterInfo.Append(Environment.NewLine);
                    }
                }

                currentCenterInfo.Append(Environment.NewLine);
                body.Append(currentCenterInfo);
            }

            return body.ToString();
        }

        private async Task SendEmail(string body, string subject)
        {
            try
            {
                _Logger.LogInformation($"Sending email: {DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")}");

                using(SmtpClient smtpClient = new SmtpClient("<smtp server>") // e.g. for gmail -- smtp.gmail.com
                {
                    Port        = 587,
                    Credentials = new NetworkCredential("<email>", "<password>"),
                    EnableSsl   = true
                })
                {
                    await smtpClient.SendMailAsync("<from email>", "<to email>", subject, body);    
                }
            }
            catch (System.Exception ex)
            {        
                await WriteToFile("Critical", ex.Message);        
                _Logger.LogCritical($"{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")} : {ex.Message}");
            }
        }

        private async Task WriteToFile(string fileNamePrepend, string content)
        {
            using(StreamWriter writer = new StreamWriter($"{_LogsDirectory}/{fileNamePrepend}_{DateTime.Now.ToString("yyyy-MM-dd_hh:mm:ss_tt")}.txt"))
            {
                await writer.WriteAsync(content);
            }
        }
    }
}
