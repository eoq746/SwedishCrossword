using System.Globalization;
using System.Text;

namespace SwedishCrossword.Api.Endpoints;

internal static class SitemapEndpoints
{
    internal static WebApplication MapSitemapEndpoints(this WebApplication app)
    {
        app.MapGet("/sitemap.xml", (PuzzleDateIndex dateIndex, TimeProvider timeProvider, HttpContext ctx) =>
        {
            var today = timeProvider.GetSwedishDate();
            var xml = GenerateSitemap(dateIndex, today);

            // Set cache headers explicitly (24 hours)
            ctx.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.Content(xml, "application/xml; charset=utf-8");
        })
        .CacheOutput("sitemap")
        .WithName("GetSitemap")
        .WithOpenApi();

        return app;
    }

    private static string GenerateSitemap(PuzzleDateIndex dateIndex, DateOnly today)
    {
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
            new { path = "/about", changefreq = "monthly", priority = 0.7 },
            new { path = "/contact", changefreq = "monthly", priority = 0.6 },
            new { path = "/privacy-policy", changefreq = "yearly", priority = 0.3 },
        };

        var todayStr = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var page in staticPages)
        {
            AddUrlEntry(sb, page.path, todayStr, page.changefreq, page.priority);
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
        sb.AppendLine("  <url>");
        sb.AppendLine($"    <loc>https://www.svensktkorsord.se{path}</loc>");
        sb.AppendLine($"    <lastmod>{lastmod}</lastmod>");
        sb.AppendLine($"    <changefreq>{changefreq}</changefreq>");
        sb.AppendLine($"    <priority>{priority.ToString("F1", CultureInfo.InvariantCulture)}</priority>");
        sb.AppendLine("  </url>");
    }
}
