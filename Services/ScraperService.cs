namespace Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Models;
using System.Globalization;
using System.Net;
using System.Threading;

public class ScraperService
{
    private static readonly HttpClient _httpClient;
    private static readonly SemaphoreSlim _throttle = new SemaphoreSlim(1, 1);
    private static readonly Random _rnd = new Random();
    private static readonly string[] _userAgents = new[]
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) Gecko/20100101 Firefox/117.0"
    };

    static ScraperService()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false
        });
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Fetches announcements from all departments asynchronously.
    /// </summary>
    /// <param name="departments">List of departments to fetch announcements from</param>
    /// <returns>A list of announcements from all departments</returns>
    public async Task<List<Announcement>> FetchAnnouncementsAsync(
        List<Department> departments)
    {
        var announcements = new List<Announcement>();
        foreach (var dept in departments)
        {
            await _throttle.WaitAsync();
            try
            {
                // random delay between 100–500ms
                await Task.Delay(_rnd.Next(100, 501));
                // rotate User-Agent
                var ua = _userAgents[_rnd.Next(_userAgents.Length)];
                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
                var deptAnnouncements = await FetchAnnouncementsForDepartmentAsync(_httpClient, dept);
                announcements.AddRange(deptAnnouncements);
            }
            catch
            {
                // ignore errors per department
            }
            finally
            {
                _throttle.Release();
            }
        }
        return announcements;
    }

    /// <summary>
    /// Fetches announcements for a specific department asynchronously.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests</param>
    /// <param name="dept">The department to fetch announcements for</param>
    /// <returns>A list of announcements for the specified department</returns>
    private async Task<List<Announcement>> FetchAnnouncementsForDepartmentAsync(
        HttpClient httpClient,
        Department dept)
    {
        var results = new List<Announcement>();
        var ajaxUri = BuildAjaxUri(dept.Url);
        int firstItem = 0;
        while (true)
        {
            var formParams = BuildFormParams(dept, firstItem);
            var request = CreateRequest(ajaxUri, dept.Url, formParams);
            var rows = await FetchRowsAsync(httpClient, request);
            if (rows == null || rows.Count == 0) break;
            results.AddRange(ParseAnnouncements(rows, dept));
            firstItem += rows.Count;

            // uncomment this after initial database bootstrap
            //if(firstItem >= 10) break; // only fetch last 10 announcements
        }
        return results;
    }

    /// <summary>
    /// Builds the Ajax URI from the page URL.
    /// </summary>
    /// <param name="pageUrl">The base page URL</param>
    /// <returns>The Ajax URI for fetching data</returns>
    private string BuildAjaxUri(string pageUrl) =>
        new Uri(new Uri(pageUrl), "/home/_TestData?langId=1").ToString();

    /// <summary>
    /// Builds form parameters for the HTTP request.
    /// </summary>
    /// <param name="dept">The department to build parameters for</param>
    /// <param name="firstItem">The index of the first item to fetch</param>
    /// <returns>A dictionary containing the form parameters</returns>
    private Dictionary<string, string> BuildFormParams(
        Department dept,
        int firstItem) => new()
    {
        { "sortOrder", "ascending" },
        { "searchString", string.Empty },
        { "insId", dept.InsId.ToString() },
        { "type", "duyuru" },
        { "firstItem", firstItem.ToString() }
    };

    /// <summary>
    /// Creates an HTTP request message for fetching announcements.
    /// </summary>
    /// <param name="ajaxUri">The Ajax URI to send the request to</param>
    /// <param name="referrerUrl">The referrer URL to use in the request</param>
    /// <param name="formParams">The form parameters to include in the request</param>
    /// <returns>An HTTP request message configured for fetching announcements</returns>
    private HttpRequestMessage CreateRequest(
        string ajaxUri,
        string referrerUrl,
        Dictionary<string, string> formParams)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ajaxUri)
        {
            Content = new FormUrlEncodedContent(formParams)
        };
        request.Headers.Referrer = new Uri(referrerUrl);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BUU_DUYURU_BOT");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Accept.ParseAdd("text/html");
        return request;
    }

    /// <summary>
    /// Fetches HTML rows from the HTTP response asynchronously.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for the request</param>
    /// <param name="request">The HTTP request message to send</param>
    /// <returns>A collection of HTML nodes representing table rows</returns>
    private async Task<HtmlNodeCollection> FetchRowsAsync(
        HttpClient httpClient,
        HttpRequestMessage request)
    {
        var response = await httpClient.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc.DocumentNode.SelectNodes("//tr");
    }

    /// <summary>
    /// Parses HTML rows into announcement objects.
    /// </summary>
    /// <param name="rows">The HTML rows to parse</param>
    /// <param name="dept">The department the announcements belong to</param>
    /// <returns>A list of parsed announcements</returns>
    private List<Announcement> ParseAnnouncements(
        HtmlNodeCollection rows,
        Department dept)
    {
        var list = new List<Announcement>();
        foreach (var row in rows)
        {
            var aNode = row.SelectSingleNode(".//td[1]//a");
            if (aNode == null) continue;
            var href = aNode.GetAttributeValue("href", string.Empty);
            var link = $"https://uludag.edu.tr/{dept.ShortName}/{href.TrimStart('/')}";
            var title = WebUtility.HtmlDecode(aNode.InnerText).Trim();
            var dateNode = row.SelectSingleNode(".//td[2]");
            if (dateNode == null) continue;
            var dateText = dateNode.InnerText.Trim();
            if (!DateTime.TryParseExact(
                dateText,
                "dd.MM.yyyy",
                CultureInfo.GetCultureInfo("tr-TR"),
                DateTimeStyles.None,
                out var addedDate))
                addedDate = DateTime.Now;
            list.Add(new Announcement
            {
                InsId = dept.InsId,
                Link = link,
                Title = title,
                AddedDate = addedDate
            });
        }
        return list;
    }
}