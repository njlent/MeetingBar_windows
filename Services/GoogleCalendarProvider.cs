using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using MeetingBar.Windows.Models;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public sealed class GoogleCalendarProvider
    {
        private const string Scope = "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/userinfo.email";
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024 };

        public bool IsConnected(CalendarSourceSettings source) => source != null && !string.IsNullOrWhiteSpace(source.GoogleRefreshTokenProtected);

        public async Task ConnectAsync(AppSettings settings, CalendarSourceSettings source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(settings.GoogleClientId)) throw new InvalidOperationException(T("Enter a Google OAuth desktop client ID first."));
            var verifier = Base64Url(RandomBytes(48));
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomBytes(24));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var redirect = "http://127.0.0.1:" + port + "/";
                var auth = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + HttpUtility.UrlEncode(settings.GoogleClientId) + "&redirect_uri=" + HttpUtility.UrlEncode(redirect) + "&response_type=code&scope=" + HttpUtility.UrlEncode(Scope) + "&access_type=offline&prompt=select_account%20consent&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + HttpUtility.UrlEncode(state);
                System.Diagnostics.Process.Start(auth);
                var clientTask = listener.AcceptTcpClientAsync();
                if (await Task.WhenAny(clientTask, Task.Delay(TimeSpan.FromMinutes(3))) != clientTask) throw new TimeoutException(T("Google sign-in timed out."));
                string requestLine;
                using (var client = clientTask.Result)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    requestLine = await reader.ReadLineAsync();
                    string header; do { header = await reader.ReadLineAsync(); } while (!string.IsNullOrEmpty(header));
                    var html = "<html><body style='font-family:Segoe UI;background:#171b26;color:white;padding:40px'><h2>" + WebUtility.HtmlEncode(T("MeetingBar connected")) + "</h2><p>" + WebUtility.HtmlEncode(T("You can close this tab.")) + "</p></body></html>";
                    var response = Encoding.UTF8.GetBytes(html);
                    var responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + response.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(responseHeader, 0, responseHeader.Length); await stream.WriteAsync(response, 0, response.Length);
                }
                if (string.IsNullOrWhiteSpace(requestLine)) throw new InvalidOperationException(T("Google returned an empty callback."));
                var target = requestLine.Split(' ')[1]; var query = HttpUtility.ParseQueryString(new Uri(redirect.TrimEnd('/') + target).Query);
                if (!string.Equals(query["state"], state, StringComparison.Ordinal)) throw new InvalidOperationException(T("Google sign-in state validation failed."));
                var code = query["code"];
                if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException(T("Google did not return an authorization code."));
                var token = await PostForm("https://oauth2.googleapis.com/token", new Dictionary<string, string> { ["code"] = code, ["client_id"] = settings.GoogleClientId, ["client_secret"] = settings.GoogleClientSecret ?? "", ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code", ["code_verifier"] = verifier });
                EnsureCalendarScope(token);
                var refresh = Value(token, "refresh_token");
                if (string.IsNullOrWhiteSpace(refresh)) throw new InvalidOperationException(T("Google did not issue a refresh token."));
                source.GoogleRefreshTokenProtected = Protect(refresh);
                source.GoogleAccountEmail = await GetEmail(Value(token, "access_token"));
                if (string.IsNullOrWhiteSpace(source.Name) || source.Name == CalendarSourceSettings.DefaultName(CalendarProviderKind.Google)) source.Name = source.GoogleAccountEmail;
            }
            finally { listener.Stop(); }
        }

        public async Task<Tuple<List<CalendarInfo>, List<CalendarEvent>>> FetchAsync(AppSettings settings, CalendarSourceSettings source, DateTime from, DateTime to)
        {
            if (!IsConnected(source)) throw new InvalidOperationException(T("Google account is not connected."));
            var access = await AccessToken(settings, source);
            var eventColors = new Dictionary<string, string>();
            try { eventColors = ParseEventColors(await GetJson("https://www.googleapis.com/calendar/v3/colors", access)); }
            catch { }
            var calendarsJson = await GetJson("https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=250", access);
            var calendars = Items(calendarsJson).Select(x => new CalendarInfo { Id = Value(x, "id"), Name = Value(x, "summary"), Source = "Google", Color = Value(x, "backgroundColor") ?? "#6D7CFF" }).ToList();
            var events = new List<CalendarEvent>();
            foreach (var calendar in calendars)
            {
                var url = "https://www.googleapis.com/calendar/v3/calendars/" + HttpUtility.UrlEncode(calendar.Id) + "/events?singleEvents=true&orderBy=startTime&maxResults=500&timeMin=" + HttpUtility.UrlEncode(from.ToUniversalTime().ToString("o")) + "&timeMax=" + HttpUtility.UrlEncode(to.ToUniversalTime().ToString("o"));
                var data = await GetJson(url, access);
                foreach (var raw in Items(data))
                {
                    var start = DateValue(raw, "start"); var end = DateValue(raw, "end");
                    var attendees = raw.ContainsKey("attendees") ? raw["attendees"] as IEnumerable : null;
                    var self = attendees?.Cast<object>().OfType<Dictionary<string, object>>().FirstOrDefault(x => BoolValue(x, "self"));
                    var response = self == null ? null : Value(self, "responseStatus");
                    var item = new CalendarEvent
                    {
                        Id = Value(raw, "id"), Title = Value(raw, "summary") ?? T("Untitled event"), Notes = Value(raw, "description"), Location = Value(raw, "location"),
                        CalendarOpenUrl = Value(raw, "htmlLink"), ConferenceUrl = ConferenceUrl(raw) ?? Value(raw, "hangoutLink"), CalendarId = calendar.Id, CalendarName = calendar.Name,
                        CalendarColor = ResolveEventColor(Value(raw, "colorId"), eventColors, calendar.Color), Organizer = NestedValue(raw, "organizer", "email"), Start = start, End = end,
                        IsAllDay = NestedValue(raw, "start", "date") != null, IsDeclined = response == "declined", IsTentative = response == "tentative" || response == "needsAction",
                        IsCancelled = Value(raw, "status") == "cancelled", IsBusy = Value(raw, "transparency") != "transparent"
                    };
                    events.Add(item);
                }
            }
            return Tuple.Create(calendars, events.OrderBy(x => x.Start).ToList());
        }

        public void Disconnect(CalendarSourceSettings source) { if (source == null) return; source.GoogleRefreshTokenProtected = ""; source.GoogleAccountEmail = ""; }
        private async Task<string> AccessToken(AppSettings settings, CalendarSourceSettings source)
        {
            var token = await PostForm("https://oauth2.googleapis.com/token", new Dictionary<string, string> { ["client_id"] = settings.GoogleClientId, ["client_secret"] = settings.GoogleClientSecret ?? "", ["refresh_token"] = Unprotect(source.GoogleRefreshTokenProtected), ["grant_type"] = "refresh_token" });
            EnsureCalendarScope(token);
            return Value(token, "access_token");
        }
        internal static bool HasCalendarScope(string scopes) => (scopes ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Contains("https://www.googleapis.com/auth/calendar.readonly");
        private static void EnsureCalendarScope(Dictionary<string, object> token)
        {
            if (HasCalendarScope(Value(token, "scope"))) return;
            throw new InvalidOperationException(T("Google connected without Calendar permission. In Settings → Calendars, complete step 3: add the calendar.readonly scope under Google Data Access. Then Disconnect and connect again."));
        }
        private async Task<Dictionary<string, object>> PostForm(string url, Dictionary<string, string> form)
        {
            var response = await _http.PostAsync(url, new FormUrlEncodedContent(form)); var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                string description = null;
                try { description = Value(_json.Deserialize<Dictionary<string, object>>(body), "error_description"); } catch { }
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(description) ? T("Google OAuth error.") : T("Google OAuth error: {0}", description));
            }
            return _json.Deserialize<Dictionary<string, object>>(body);
        }
        private async Task<Dictionary<string, object>> GetJson(string url, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await _http.SendAsync(request); var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ApiErrorMessage(response.StatusCode, body));
            return _json.Deserialize<Dictionary<string, object>>(body);
        }
        internal static string ApiErrorMessage(HttpStatusCode status, string body)
        {
            var reason = "";
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                var error = root != null && root.ContainsKey("error") ? root["error"] as Dictionary<string, object> : null;
                var errors = error != null && error.ContainsKey("errors") ? error["errors"] as IEnumerable : null;
                var first = errors?.Cast<object>().OfType<Dictionary<string, object>>().FirstOrDefault();
                reason = Value(first, "reason") ?? "";
            }
            catch { }

            switch (reason)
            {
                case "accessNotConfigured":
                    return T("Google Calendar API is not enabled for this OAuth project. In Settings → Calendars, complete step 2 in the same Google Cloud project as the Desktop client. Click Enable, wait a minute, then Refresh.");
                case "insufficientPermissions":
                    return T("Google connected without Calendar permission. In Settings → Calendars, complete step 3 under Data Access, then Disconnect and connect again.");
                case "dailyLimitExceeded":
                case "rateLimitExceeded":
                case "userRateLimitExceeded":
                    return T("Google Calendar API quota is temporarily exhausted. Try Refresh later.");
                default:
                    return T("Google Calendar error: {0}", status);
            }
        }
        internal static Dictionary<string, string> ParseEventColors(Dictionary<string, object> root)
        {
            var result = new Dictionary<string, string>();
            var colors = root != null && root.ContainsKey("event") ? root["event"] as Dictionary<string, object> : null;
            if (colors == null) return result;
            foreach (var pair in colors)
            {
                var value = Value(pair.Value as Dictionary<string, object>, "background");
                if (!string.IsNullOrWhiteSpace(value)) result[pair.Key] = value;
            }
            return result;
        }
        internal static string ResolveEventColor(string colorId, Dictionary<string, string> eventColors, string fallback)
        {
            string value;
            return !string.IsNullOrWhiteSpace(colorId) && eventColors != null && eventColors.TryGetValue(colorId, out value) ? value : fallback;
        }
        private async Task<string> GetEmail(string token) { var json = await GetJson("https://www.googleapis.com/oauth2/v2/userinfo", token); return Value(json, "email"); }
        private static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> x) => x.ContainsKey("items") ? ((IEnumerable)x["items"]).Cast<object>().OfType<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>();
        private static string Value(Dictionary<string, object> x, string key) => x != null && x.ContainsKey(key) ? Convert.ToString(x[key]) : null;
        private static bool BoolValue(Dictionary<string, object> x, string key) => x != null && x.ContainsKey(key) && Convert.ToBoolean(x[key]);
        private static string NestedValue(Dictionary<string, object> x, string key, string nested) => x.ContainsKey(key) ? Value(x[key] as Dictionary<string, object>, nested) : null;
        private static DateTime DateValue(Dictionary<string, object> x, string key) { var date = NestedValue(x, key, "dateTime") ?? NestedValue(x, key, "date"); DateTime parsed; return DateTime.TryParse(date, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed) ? parsed.ToLocalTime() : DateTime.MinValue; }
        private static string ConferenceUrl(Dictionary<string, object> x)
        {
            if (!x.ContainsKey("conferenceData")) return null; var data = x["conferenceData"] as Dictionary<string, object>;
            if (data == null || !data.ContainsKey("entryPoints")) return null;
            return ((IEnumerable)data["entryPoints"]).Cast<object>().OfType<Dictionary<string, object>>().Where(y => Value(y, "entryPointType") == "video").Select(y => Value(y, "uri")).FirstOrDefault();
        }
        private static byte[] RandomBytes(int count) { var data = new byte[count]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data); return data; }
        private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        private static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }
}
