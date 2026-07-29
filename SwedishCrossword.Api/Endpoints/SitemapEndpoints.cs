using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SwedishCrossword.Api.Endpoints;

internal static class SitemapEndpoints
{
    private const string SiteBaseUrl = "https://www.svensktkorsord.se";

    internal static WebApplication MapSitemapEndpoints(this WebApplication app)
    {
        app.MapGet("/sitemap.xml", (PuzzleDateIndex dateIndex, TimeProvider timeProvider, IWebHostEnvironment env, HttpContext ctx) =>
        {
            var today = timeProvider.GetSwedishDate();
            var xml = GenerateSitemap(dateIndex, env.WebRootPath, today);

            // Set cache headers explicitly (24 hours)
            ctx.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.Content(xml, "application/xml; charset=utf-8");
        })
        .CacheOutput("sitemap")
        .WithName("GetSitemap");

        return app;
    }

    private static string GenerateSitemap(PuzzleDateIndex dateIndex, string webRootPath, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(dateIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(webRootPath);

        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

        // Static pages with fixed metadata
        var staticPages = new[]
        {
            new { path = "/", changefreq = "daily", priority = 1.0 },
            new { path = "/play", changefreq = "weekly", priority = 0.95 },
            new { path = "/puzzle", changefreq = "daily", priority = 0.9 },
            new { path = "/leaderboard", changefreq = "hourly", priority = 0.8 },
            new { path = "/calendar", changefreq = "daily", priority = 0.85 },
            new { path = "/guides", changefreq = "weekly", priority = 0.8 },
            new { path = "/lexicon", changefreq = "daily", priority = 0.85 },
            new { path = "/about", changefreq = "monthly", priority = 0.7 },
            new { path = "/contact", changefreq = "monthly", priority = 0.6 },
            new { path = "/privacy-policy", changefreq = "yearly", priority = 0.3 },
        };

        var todayStr = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var page in staticPages)
        {
            AddUrlEntry(sb, page.path, todayStr, page.changefreq, page.priority);
        }

        foreach (var guideEntry in LoadGuideEntries(webRootPath))
        {
            AddUrlEntry(sb, $"/guides/{guideEntry.Slug}", guideEntry.LastModified, "monthly", 0.7);
        }

        foreach (var lexiconSlug in LoadLexiconSlugs(webRootPath))
        {
            AddUrlEntry(sb, $"/lexicon/{lexiconSlug}", todayStr, "weekly", 0.7);
        }

        // Dynamic puzzle archive dates
        var puzzleDates = dateIndex.GetDates(today);
        foreach (var entry in puzzleDates)
        {
            // Puzzle archive pages are immutable after publication
            AddUrlEntry(sb, $"/puzzle/{entry.Date}", entry.Date, "never", 0.7);
        }

        sb.AppendLine("</urlset>");
        return sb.ToString();
    }

    private static void AddUrlEntry(StringBuilder sb, string path, string lastmod, string changefreq, double priority)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <url>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <loc>{SiteBaseUrl}{path}</loc>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <lastmod>{lastmod}</lastmod>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <changefreq>{changefreq}</changefreq>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <priority>{priority.ToString("F1", CultureInfo.InvariantCulture)}</priority>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  </url>");
    }

    private static IReadOnlyList<SitemapGuideEntry> LoadGuideEntries(string webRootPath)
    {
        var path = Path.Combine(webRootPath, "app", "guides", "index.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);

            if (!json.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<SitemapGuideEntry>();
            foreach (var entry in entries.EnumerateArray())
            {
                var slug = entry.TryGetProperty("slug", out var slugElement)
                    ? slugElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                var published = entry.TryGetProperty("published", out var publishedElement)
                    ? publishedElement.GetString()
                    : null;

                var lastModified = DateOnly.TryParseExact(
                        published,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var publishedDate)
                    ? publishedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : DateOnly.FromDateTime(File.GetLastWriteTimeUtc(path)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                result.Add(new SitemapGuideEntry(slug, lastModified));
            }

            return result;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> LoadLexiconSlugs(string webRootPath)
    {
        var path = Path.Combine(webRootPath, "app", "lexicon-data", "index.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);

            if (!json.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("slug", out var slugElement))
                {
                    continue;
                }

                var slug = slugElement.GetString();
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                slugs.Add(slug);
            }

            return slugs.Count > 0
                ? [.. slugs.OrderBy(value => value, StringComparer.Ordinal)]
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record SitemapGuideEntry(string Slug, string LastModified);
}
