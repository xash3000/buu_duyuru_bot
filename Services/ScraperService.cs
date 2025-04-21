namespace Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Models;
using System.Globalization;
using System.Net;

public class ScraperService
{
    public async Task<List<Announcement>> FetchAnnouncementsAsync(List<Department> departments)
    {
        var announcements = new List<Announcement>();
        using var httpClient = new HttpClient();
        foreach (var dept in departments)
        {
            try
            {
                var deptAnnouncements = await FetchAnnouncementsForDepartmentAsync(httpClient, dept);
                announcements.AddRange(deptAnnouncements);
            }
            catch
            {
                // ignore errors per department
            }
        }
        return announcements;
    }

    private async Task<List<Announcement>> FetchAnnouncementsForDepartmentAsync(HttpClient httpClient, Department dept)
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
            results.AddRange(ParseAnnouncements(rows, dept.Name, dept.ShortName));
            firstItem += rows.Count;
        }
        return results;
    }

    private string BuildAjaxUri(string pageUrl) => new Uri(new Uri(pageUrl), "/home/_TestData?langId=1").ToString();

    private Dictionary<string, string> BuildFormParams(Department dept, int firstItem) => new()
    {
        { "sortOrder", "ascending" },
        { "searchString", string.Empty },
        { "insId", dept.InsId.ToString() },
        { "type", "duyuru" },
        { "firstItem", firstItem.ToString() }
    };

    private HttpRequestMessage CreateRequest(string ajaxUri, string referrerUrl, Dictionary<string, string> formParams)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ajaxUri)
        {
            Content = new FormUrlEncodedContent(formParams)
        };
        request.Headers.Referrer = new Uri(referrerUrl);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) BUU_DUYURU_BOT");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Accept.ParseAdd("text/html");
        return request;
    }

    private async Task<HtmlNodeCollection> FetchRowsAsync(HttpClient httpClient, HttpRequestMessage request)
    {
        var response = await httpClient.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc.DocumentNode.SelectNodes("//tr");
    }

    private List<Announcement> ParseAnnouncements(HtmlNodeCollection rows, string departmentName, string departmentShortName)
    {
        var list = new List<Announcement>();
        foreach (var row in rows)
        {
            var aNode = row.SelectSingleNode(".//td[1]//a");
            if (aNode == null) continue;
            // build full link with short name prefix
            var href = aNode.GetAttributeValue("href", string.Empty);
            var link = $"https://uludag.edu.tr/{departmentShortName}/{href.TrimStart('/')}";
            // Decode HTML entities in title
            var title = WebUtility.HtmlDecode(aNode.InnerText).Trim();
            var dateNode = row.SelectSingleNode(".//td[2]");
            if (dateNode == null) continue;
            var dateText = dateNode.InnerText.Trim();
            if (!DateTime.TryParseExact(dateText, "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var addedDate))
                addedDate = DateTime.Now;
            list.Add(new Announcement { Department = departmentName, DepartmentShortName = departmentShortName, Link = link, Title = title, AddedDate = addedDate });
        }
        return list;
    }
}