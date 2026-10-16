using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace AMPMWeb.Controllers;

// Update feed for the Helpdesk mobile app.
//
// The app's APK is built by GitHub Actions (mobile_app/ in this repository) and
// published as a GitHub Release tagged "app-v<version>". The phone app asks THIS
// server which version is newest and where to download it, so it never needs to
// know about GitHub, tokens or whether the repository is public or private.
//
//   GET /api/app/latest    -> {version, notes, size, sha256, mandatory, apkUrl}
//   GET /api/app/download  -> redirects to the newest APK (always the latest file)
//   GET /app               -> small public page with the download button (share this link)
//
// Public on purpose (no login): it only reveals the version number and hands out
// the same APK the company installs on every phone. Exempt in ModulePermissionFilter.
//
// Optional Render environment variables:
//   AMPM_GITHUB_TOKEN  read-only token (Contents: read) - only needed once the
//                      repository is made PRIVATE
//   AMPM_APP_REPO      "owner/name" of the repository (default sandy199012/ampm-qh-serve)
public class AppUpdateController : Controller
{
    static string Repo => Environment.GetEnvironmentVariable("AMPM_APP_REPO") is { Length: > 3 } r ? r : "sandy199012/ampm-qh-serve";
    static string? Token => Environment.GetEnvironmentVariable("AMPM_GITHUB_TOKEN") is { Length: > 5 } t ? t : null;

    // No auto-redirect: for the download we want GitHub's short-lived file URL, not the file itself.
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    sealed record Rel(string Version, string Notes, string Sha256, bool Mandatory, long AssetId, long Size, string? DirectUrl = null);

    static Rel? _cache;
    static DateTime _cacheUntil = DateTime.MinValue;
    static readonly SemaphoreSlim Gate = new(1, 1);

    static HttpRequestMessage Gh(string url, string accept)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("AMPM-IT-Tool", "1.0"));
        req.Headers.Accept.ParseAdd(accept);
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (Token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return req;
    }

    // Newest published release whose tag starts with "app-v" and that carries an .apk.
    static async Task<Rel?> LoadAsync()
    {
        if (_cache != null && DateTime.UtcNow < _cacheUntil) return _cache;
        await Gate.WaitAsync();
        try
        {
            if (_cache != null && DateTime.UtcNow < _cacheUntil) return _cache;
            Rel? found = null;
            try
            {
                using var res = await Http.SendAsync(Gh($"https://api.github.com/repos/{Repo}/releases?per_page=20", "application/vnd.github+json"));
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    foreach (var r in doc.RootElement.EnumerateArray())
                    {
                        var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                        if (!tag.StartsWith("app-v", StringComparison.OrdinalIgnoreCase)) continue;
                        if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                        if (r.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()) continue;
                        if (!r.TryGetProperty("assets", out var assets)) continue;
                        foreach (var a in assets.EnumerateArray())
                        {
                            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
                            var body = (r.TryGetProperty("body", out var b) ? b.GetString() : null) ?? "";
                            body = body.Replace("\r", "").Trim();
                            var sha = "";
                            var m = Regex.Match(body, @"sha256:\s*([0-9a-fA-F]{64})");
                            if (m.Success) { sha = m.Groups[1].Value.ToLowerInvariant(); body = body.Replace(m.Value, "").Trim(); }
                            var mandatory = body.Contains("[mandatory]", StringComparison.OrdinalIgnoreCase);
                            body = Regex.Replace(body, @"\[mandatory\]", "", RegexOptions.IgnoreCase).Trim();
                            found = new Rel(tag.Substring(5), body, sha, mandatory,
                                a.GetProperty("id").GetInt64(),
                                a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0);
                            break;
                        }
                        if (found != null) break;
                    }
                }
            }
            catch { /* GitHub unreachable: fall through and keep the previous answer if any */ }

            // The GitHub API is rate-limited per IP (60/hour without a token) and Render's
            // shared IPs often hit that limit. The public releases feed and the plain
            // download link are not limited, so use them when the API gave no answer.
            if (found == null && Token == null) found = await LoadFromAtomAsync();

            if (found != null) { _cache = found; _cacheUntil = DateTime.UtcNow.AddMinutes(5); }
            else if (_cache != null) _cacheUntil = DateTime.UtcNow.AddMinutes(1);   // keep serving the last known release
            return _cache;
        }
        finally { Gate.Release(); }
    }


    // Fallback for a PUBLIC repository: releases.atom (newest first) + direct download link.
    static async Task<Rel?> LoadFromAtomAsync()
    {
        try
        {
            using var res = await Http.SendAsync(Gh($"https://github.com/{Repo}/releases.atom", "application/atom+xml"));
            if (!res.IsSuccessStatusCode) return null;
            var xml = await res.Content.ReadAsStringAsync();
            foreach (Match e in Regex.Matches(xml, @"<entry>(.*?)</entry>", RegexOptions.Singleline))
            {
                var block = e.Groups[1].Value;
                var lm = Regex.Match(block, @"/releases/tag/([^""<\s]+)");
                if (!lm.Success) continue;
                var tag = Uri.UnescapeDataString(lm.Groups[1].Value);
                if (!tag.StartsWith("app-v", StringComparison.OrdinalIgnoreCase)) continue;
                var cm = Regex.Match(block, @"<content[^>]*>(.*?)</content>", RegexOptions.Singleline);
                var body = cm.Success ? System.Net.WebUtility.HtmlDecode(cm.Groups[1].Value) : "";
                body = Regex.Replace(body, @"<\s*(br|/p|/li|/div)\s*/?>", "\n", RegexOptions.IgnoreCase);
                body = Regex.Replace(body, @"<[^>]+>", "");
                body = System.Net.WebUtility.HtmlDecode(body).Replace("\r", "").Trim();
                var sha = "";
                var m = Regex.Match(body, @"sha256:\s*([0-9a-fA-F]{64})");
                if (m.Success) { sha = m.Groups[1].Value.ToLowerInvariant(); body = body.Replace(m.Value, "").Trim(); }
                var mandatory = body.Contains("[mandatory]", StringComparison.OrdinalIgnoreCase);
                body = Regex.Replace(body, @"\[mandatory\]", "", RegexOptions.IgnoreCase).Trim();
                var url = $"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/AMPM_Helpdesk.apk";
                // size: ask the download link itself (headers only)
                long size = 0;
                try
                {
                    using var head = await Http.SendAsync(Gh(url, "application/octet-stream"), HttpCompletionOption.ResponseHeadersRead);
                    var loc = head.Headers.Location;
                    if (loc != null)
                    {
                        using var h2 = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, loc), HttpCompletionOption.ResponseHeadersRead);
                        size = h2.Content.Headers.ContentLength ?? 0;
                    }
                }
                catch { }
                return new Rel(tag.Substring(5), body, sha, mandatory, 0, size, url);
            }
        }
        catch { }
        return null;
    }

    [HttpGet("/api/app/latest")]
    public async Task<IActionResult> Latest()
    {
        var r = await LoadAsync();
        if (r == null) return NotFound(new { ok = false, error = "No app release published yet" });
        Response.Headers["Cache-Control"] = "no-store";
        return Json(new { ok = true, version = r.Version, notes = r.Notes, size = r.Size, sha256 = r.Sha256, mandatory = r.Mandatory, apkUrl = "/api/app/download" });
    }

    [HttpGet("/api/app/download")]
    public async Task<IActionResult> Download()
    {
        var r = await LoadAsync();
        if (r == null) return NotFound("No app release published yet.");
        if (r.DirectUrl != null) { Response.Headers["Cache-Control"] = "no-store"; return Redirect(r.DirectUrl); }
        try
        {
            using var res = await Http.SendAsync(Gh($"https://api.github.com/repos/{Repo}/releases/assets/{r.AssetId}", "application/octet-stream"),
                                                  HttpCompletionOption.ResponseHeadersRead);
            var loc = res.Headers.Location;
            if ((int)res.StatusCode is 301 or 302 or 303 or 307 or 308 && loc != null)
            {
                Response.Headers["Cache-Control"] = "no-store";
                return Redirect(loc.ToString());
            }
        }
        catch { }
        return StatusCode(502, "Could not reach the app release. Try again in a minute.");
    }

    [HttpGet("/app")]
    public async Task<IActionResult> Page()
    {
        var r = await LoadAsync();
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var body = r == null
            ? "<p class='muted'>The app is not published yet. Please check again later.</p>"
            : $@"<a class='btn' href='/api/app/download'>Download AMPM Helpdesk app</a>
<p class='muted'>Version {E(r.Version)} &middot; {(r.Size > 0 ? (r.Size / 1048576.0).ToString("0.0") + " MB" : "")}</p>
{(r.Notes.Length > 0 ? "<div class='notes'><b>What's new</b><br>" + E(r.Notes).Replace("\n", "<br>") + "</div>" : "")}
<ol><li>Tap the button and open the downloaded file.</li>
<li>If Android asks, allow <b>Install unknown apps</b> for your browser.</li>
<li>Tap <b>Install</b>, open the app and log in with your Employee Code.</li></ol>
<p class='muted'>Already installed? You don't need this page &mdash; the app offers updates by itself.</p>";
        var html = $@"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>AMPM Helpdesk app</title>
<style>body{{font-family:system-ui,Segoe UI,Roboto,sans-serif;background:#0A192F;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:16px}}
.c{{background:#fff;border-radius:20px;padding:28px 24px;max-width:420px;width:100%;box-shadow:0 20px 60px rgba(0,0,0,.4)}}
h1{{margin:0 0 4px;font-size:21px;color:#0A192F}}.brand{{color:#06B6D4;font-size:12px;letter-spacing:2px;margin-bottom:18px}}
.btn{{display:block;text-align:center;background:#2563EB;color:#fff;text-decoration:none;padding:14px;border-radius:12px;font-weight:600;font-size:16px}}
.muted{{color:#64748B;font-size:13px}}.notes{{background:#F3F6FA;border-radius:12px;padding:12px;font-size:13px;margin:12px 0}}ol{{font-size:14px;color:#334155;padding-left:20px;line-height:1.6}}</style></head>
<body><div class='c'><h1>AMPM IT Helpdesk</h1><div class='brand'>ANDROID APP</div>{body}</div></body></html>";
        return Content(html, "text/html; charset=utf-8");
    }
}
