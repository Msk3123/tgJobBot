using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SystematiaBot.Infrastructure.Gmail;

// One-time Gmail authorization for the bot.
//   dotnet run --project src/SystematiaBot.Cli -- gmail-auth --client-id <id> --client-secret <secret>
// Opens Google's consent page, receives the code on a loopback port and prints the refresh token
// that goes into the server's Gmail__RefreshToken. Nothing is written to disk.

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] != "gmail-auth")
{
    Console.WriteLine("Usage: SystematiaBot.Cli gmail-auth --client-id <id> --client-secret <secret>");
    Console.WriteLine("       (or set Gmail:ClientId / Gmail:ClientSecret via user-secrets or env Gmail__ClientId / Gmail__ClientSecret)");
    return 1;
}

var config = new ConfigurationBuilder()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args[1..], new Dictionary<string, string> { ["--client-id"] = "Gmail:ClientId", ["--client-secret"] = "Gmail:ClientSecret" })
    .Build();

var clientId = config["Gmail:ClientId"];
var clientSecret = config["Gmail:ClientSecret"];
if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    Console.Error.WriteLine("Gmail:ClientId and Gmail:ClientSecret are required (OAuth client of type \"Desktop app\" in Google Cloud Console).");
    return 1;
}

// Loopback redirect on a free port: allowed for "Desktop app" clients without registering the exact URI.
var port = FreePort();
var redirectUri = $"http://127.0.0.1:{port}/";
var (verifier, challenge) = GoogleOAuth.CreatePkce();
var state = GoogleOAuth.CreateState();
var consentUrl = GoogleOAuth.BuildConsentUrl(clientId, redirectUri, state, challenge);

using var listener = new HttpListener();
listener.Prefixes.Add(redirectUri);
listener.Start();

Console.WriteLine("1) Відкрий у браузері (і увійди в акаунт, де лежать листи Djinni):\n");
Console.WriteLine(consentUrl + "\n");
TryOpenBrowser(consentUrl);
Console.WriteLine("2) Чекаю на відповідь Google (5 хвилин)…");

var contextTask = listener.GetContextAsync();
if (await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5))) != contextTask)
{
    Console.Error.WriteLine("Час вийшов.");
    return 1;
}

var context = await contextTask;
var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
var ok = query["error"] is null && query["state"] == state && !string.IsNullOrEmpty(query["code"]);
await Respond(context, ok ? "✅ Готово. Можна закрити цю вкладку і повернутись у термінал." : "❌ Помилка авторизації. Подивись термінал.");
listener.Stop();

if (query["error"] is { } error)
{
    Console.Error.WriteLine($"Google повернув помилку: {error}");
    return 1;
}

if (query["state"] != state)
{
    // A different state means the redirect did not come from the consent page we opened.
    Console.Error.WriteLine("state не збігається — відповідь відхилено.");
    return 1;
}

using var http = new HttpClient();
GoogleToken token;
try
{
    token = await GoogleOAuth.ExchangeCodeAsync(http, clientId, clientSecret, query["code"]!, verifier, redirectUri, CancellationToken.None);
}
catch (GoogleOAuthException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

if (string.IsNullOrEmpty(token.RefreshToken))
{
    Console.Error.WriteLine("Google не повернув refresh token. Прибери доступ застосунку на https://myaccount.google.com/permissions і запусти ще раз.");
    return 1;
}

var email = await GetEmailAsync(http, token.AccessToken);
Console.WriteLine($"\n✅ Підключено акаунт: {email ?? "(не вдалося прочитати адресу)"}");
Console.WriteLine($"   Права: {token.Scope}\n");
Console.WriteLine("3) Додай на сервері в .env (це як пароль від пошти на читання — нікому не показуй і не коміть):\n");
Console.WriteLine($"Gmail__RefreshToken={token.RefreshToken}");
return 0;

static int FreePort()
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}

static void TryOpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception)
    {
        // Headless machine: the URL is printed above.
    }
}

static async Task Respond(HttpListenerContext context, string message)
{
    var html = $"<!doctype html><meta charset=utf-8><body style=\"font-family:sans-serif;padding:40px\"><h2>{WebUtility.HtmlEncode(message)}</h2></body>";
    var bytes = Encoding.UTF8.GetBytes(html);
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.ContentLength64 = bytes.Length;
    await context.Response.OutputStream.WriteAsync(bytes);
    context.Response.Close();
}

static async Task<string?> GetEmailAsync(HttpClient http, string accessToken)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://gmail.googleapis.com/gmail/v1/users/me/profile");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    using var response = await http.SendAsync(request);
    if (!response.IsSuccessStatusCode)
        return null;
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.TryGetProperty("emailAddress", out var e) ? e.GetString() : null;
}
