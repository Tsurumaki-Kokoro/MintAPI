using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi;
using Ossapi.Auth;

// Usage: <repository-root> <cases.json> <output-directory> [--replay]
if (args.Length < 3) throw new ArgumentException("Specify repository root, cases JSON, output directory, and optionally --replay.");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[2]);
var replay = args.Contains("--replay");
Directory.CreateDirectory(output);
var cases = JArray.Parse(await File.ReadAllTextAsync(args[1]));
var settings = OssapiClient.BuildJsonSettings();
var serializer = JsonSerializer.Create(settings);
HttpMessageHandler inner;
if (replay) inner = new HttpClientHandler();
else
{
    var config = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(root, "HitCircleAPI/appsettings.Development.json")));
    inner = new OssapiAuthHandler(config["OsuApi"]!["ClientId"]!.Value<int>(),
        config["OsuApi"]!["ClientSecret"]!.Value<string>()!, "https://osu.ppy.sh/oauth/token", new TokenStore(Path.Combine(output, "tokens")))
        { InnerHandler = new HttpClientHandler() };
}
using var capture = new CaptureHandler(output, replay) { InnerHandler = inner };
using var http = new HttpClient(capture) { BaseAddress = new Uri("https://osu.ppy.sh/api/v2/"), Timeout = TimeSpan.FromSeconds(45) };
http.DefaultRequestHeaders.Add("x-api-version", "20241024");
http.DefaultRequestHeaders.UserAgent.ParseAdd("Ossapi.ContractValidation/1.0");
using var client = new OssapiClient(http);
var records = new JArray();
foreach (var item in cases.Cast<JObject>())
{
    var name = item["name"]!.Value<string>()!;
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9_-]+$"))
        throw new ArgumentException("Case names must contain only letters, digits, underscores, and hyphens.");
    var methodName = item["method"]!.Value<string>()!;
    if (!methodName.StartsWith("Get") && !methodName.StartsWith("Search"))
        throw new InvalidOperationException("Only read/compute methods are allowed.");
    if (methodName is "SendPmAsync" or "SendAnnouncementAsync" or "ForumCreateTopicAsync" or "ForumReplyAsync" or "ForumEditTopicAsync" or "ForumEditPostAsync" or "GetFriendsAsync" or "GetMeAsync")
        throw new InvalidOperationException("This runner only permits public read/compute cases.");
    capture.Name = name;
    capture.LastResponse = null;
    var record = new JObject { ["name"] = name, ["method"] = methodName, ["arguments"] = item["arguments"]?.DeepClone(), ["replay"] = replay };
    try
    {
        var input = item["arguments"] as JObject ?? new();
        var methods = typeof(OssapiClient).GetMethods().Where(m => m.Name == methodName).ToList();
        // Use the string user overload; the integer overload delegates to it.
        var method = methods.OrderByDescending(m => m.GetParameters().Count(p => input.ContainsKey(p.Name!))).First(m => m.GetParameters().All(p => p.Name != "user" || p.ParameterType == typeof(string)));
        var parameters = method.GetParameters().Select(p => input.TryGetValue(p.Name!, out var value)
            ? ConvertArgument(value, p.ParameterType, serializer)
            : p.HasDefaultValue ? DefaultArgument(p) : throw new ArgumentException($"Missing {p.Name}")).ToArray();
        var task = (Task)method.Invoke(client, parameters)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var outputSerializer = JsonSerializer.Create(OssapiClient.BuildJsonSettings());
        outputSerializer.NullValueHandling = NullValueHandling.Include;
        var modeled = JToken.FromObject(result, outputSerializer);
        await File.WriteAllTextAsync(Path.Combine(output, name + ".modeled.json"), modeled.ToString());
        record["status"] = "deserialized";
        record["resultType"] = result.GetType().FullName;
        record["resultCount"] = result is System.Collections.ICollection list ? list.Count : null;
        // Wrapper-removing methods require an explicit raw root path.
        var raw = JToken.Parse(capture.LastResponse!["body"]!.Value<string>()!);
        var rootPath = item["resultPath"]?.Value<string>();
        if (!string.IsNullOrEmpty(rootPath)) raw = raw.SelectToken(rootPath) ?? throw new Exception("Result path missing.");
        var differences = new HashSet<string>();
        Compare(raw, modeled, "$", differences);
        record["differences"] = new JArray(differences.OrderBy(x => x).Take(500));
        record["differenceCount"] = differences.Count;
    }
    catch (Exception error)
    {
        var actual = error is TargetInvocationException invoke ? invoke.InnerException! : error;
        record["status"] = "failed";
        record["errorType"] = actual.GetType().Name;
        // Exception text may contain user data; retain locally, do not print it.
        record["error"] = actual.Message;
    }
    if (capture.LastResponse != null)
    {
        record["httpStatus"] = capture.LastResponse["status"];
        record["responseSha256"] = capture.LastResponse["sha256"];
        record["request"] = capture.LastResponse["request"];
    }
    records.Add(record);
    Console.WriteLine($"{name}: {record["status"]}, HTTP {record["httpStatus"]}, differences {record["differenceCount"]}");
    await File.WriteAllTextAsync(Path.Combine(output, replay ? "replay-report.json" : "report.json"), records.ToString());
    if (capture.LastResponse?["status"]?.Value<int>() == 429)
        throw new InvalidOperationException("Server rate limited this batch. Captured Retry-After locally; no further requests were sent.");
}
if (records.Any(r => r["status"]?.Value<string>() != "deserialized" || r["differenceCount"]?.Value<int>() != 0))
    Environment.ExitCode = 1;

static object? DefaultArgument(ParameterInfo parameter)
{
    var value = parameter.DefaultValue;
    var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
    return value != null && type.IsEnum ? Enum.ToObject(type, value) : value;
}
static object? ConvertArgument(JToken value, Type type, JsonSerializer serializer)
{
    var actual = Nullable.GetUnderlyingType(type) ?? type;
    if (value is JArray items && actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(IEnumerable<>))
    {
        var elementType = actual.GetGenericArguments()[0];
        var array = Array.CreateInstance(elementType, items.Count);
        for (var i = 0; i < items.Count; i++) array.SetValue(ConvertArgument(items[i], elementType, serializer), i);
        return array;
    }
    if (actual == typeof(Ossapi.Mods.Mod)) return Ossapi.Mods.Mod.Parse(value.Value<string>()!);
    if (actual == typeof(Ossapi.Enums.GameMode))
        return value.Value<string>() switch { "osu" => Ossapi.Enums.GameMode.Osu, "taiko" => Ossapi.Enums.GameMode.Taiko, "fruits" => Ossapi.Enums.GameMode.Catch, "mania" => Ossapi.Enums.GameMode.Mania, _ => throw new ArgumentException("Unknown mode") };
    if (actual.IsEnum && value.Type == JTokenType.String && Enum.TryParse(actual, value.Value<string>(), true, out var parsed)) return parsed;
    return value.ToObject(type, serializer);
}
static void Compare(JToken raw, JToken modeled, string path, HashSet<string> diffs)
{
    if (raw is JObject source && modeled is JObject target)
    {
        foreach (var p in source.Properties())
            if (!target.TryGetValue(p.Name, out var other)) diffs.Add(path + "." + p.Name + ": unmodeled");
            else Compare(p.Value, other, path + "." + p.Name, diffs);
    }
    else if (raw is JArray a && modeled is JArray b)
    {
        if (a.Count != b.Count) diffs.Add(path + ": array length differs");
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++) Compare(a[i]!, b[i]!, path + "[]", diffs);
    }
    else if (raw.Type == JTokenType.Null)
    {
        if (modeled.Type != JTokenType.Null) diffs.Add(path + ": null became a value");
    }
    else if (raw.Type == JTokenType.Date && modeled.Type == JTokenType.String)
    {
        if (raw.ToObject<DateTimeOffset>() == DateTimeOffset.Parse(modeled.Value<string>()!, System.Globalization.CultureInfo.InvariantCulture)) return;
        diffs.Add(path + ": date differs");
    }
    else if (raw.Type is JTokenType.Integer or JTokenType.Float && modeled.Type is JTokenType.Integer or JTokenType.Float)
    {
        if (raw.Value<decimal>() != modeled.Value<decimal>()) diffs.Add(path + ": numeric value differs");
    }
    else if (path.EndsWith(".mods") && modeled.Type == JTokenType.Integer)
    {
        var mod = raw.ToObject<Ossapi.Mods.Mod>(JsonSerializer.Create(OssapiClient.BuildJsonSettings()));
        if (mod.Value != modeled.Value<int>()) diffs.Add(path + ": mods differ");
    }
    else if (path.EndsWith(".status") && raw.Type == JTokenType.String && modeled.Type == JTokenType.Integer)
    {
        var normalized = raw.ToObject<Ossapi.Enums.RankStatus>(JsonSerializer.Create(OssapiClient.BuildJsonSettings()));
        if ((int)normalized != modeled.Value<int>()) diffs.Add(path + ": rank status differs");
    }
    else if (!JToken.DeepEquals(raw, modeled)) diffs.Add(path + ": value or representation differs");
}
sealed class CaptureHandler(string directory, bool replay) : DelegatingHandler
{
    public string Name = "";
    public JObject? LastResponse;
    private DateTimeOffset _lastRequest;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var file = Path.Combine(directory, Name + ".raw.json");
        if (replay)
        {
            LastResponse = JObject.Parse(await File.ReadAllTextAsync(file, ct));
            var expected = LastResponse["request"]!.Value<string>();
            if (expected != request.Method + " " + request.RequestUri) throw new InvalidOperationException("Replay request does not match captured request.");
            return new HttpResponseMessage((System.Net.HttpStatusCode)LastResponse["status"]!.Value<int>())
            { Content = new StringContent(LastResponse["body"]!.Value<string>()!), RequestMessage = request };
        }
        var delay = TimeSpan.FromMilliseconds(1100) - (DateTimeOffset.UtcNow - _lastRequest);
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        _lastRequest = DateTimeOffset.UtcNow;
        var requestIdentity = request.Method + " " + request.RequestUri;
        var response = await base.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        LastResponse = new JObject {
            ["request"] = requestIdentity,
            ["effectiveUrl"] = response.RequestMessage?.RequestUri?.ToString(),
            ["requestBody"] = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct),
            ["apiVersion"] = string.Join(",", request.Headers.TryGetValues("x-api-version", out var versions) ? versions : []),
            ["capturedAt"] = DateTimeOffset.UtcNow, ["status"] = (int)response.StatusCode,
            ["retryAfter"] = response.Headers.RetryAfter?.ToString(),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))), ["body"] = body };
        await File.WriteAllTextAsync(file, LastResponse.ToString(), ct);
        return response;
    }
}
