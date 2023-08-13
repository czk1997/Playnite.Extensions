using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;

namespace FanzaMetadata;

public class Scrapper
{
    private readonly ILogger<Scrapper> _logger;
    private readonly IConfiguration _configuration;

    public const string GameBaseUrl = "https://dlsoft.dmm.co.jp/detail/";
    public const string IconUrlFormat = "https://pics.dmm.co.jp/digital/game/d_{0}/d_{0}pt.jpg";
    public const string SearchBaseUrl = "https://www.dmm.co.jp/search/=/searchstr=";

    public Scrapper(ILogger<Scrapper> logger, HttpMessageHandler messageHandler)
    {
        _logger = logger;

        _configuration = Configuration.Default
            .WithRequesters(messageHandler)
            .WithDefaultLoader();
    }

    public async Task<ScrapperResult> ScrapGamePage(string id, CancellationToken cancellationToken = default)
    {
        var url = GameBaseUrl + id;

        var context = BrowsingContext.New(_configuration);
        var document = await context.OpenAsync(url, cancellationToken);
        var result = new ScrapperResult
        {
            Link = url
        };
        _logger.LogWarning("Get URL Done");
        // Get title
        var productTitleElement = document
            .GetElementsByClassName("productTitle__headline")
            .FirstOrDefault(elem => elem.TagName.Equals(TagNames.H1, StringComparison.OrdinalIgnoreCase));
        _logger.LogWarning("Get Title Done");
        if (productTitleElement is not null)
        {
            var productTitleText = productTitleElement.Text();
            // Removed campaign since no longer useful
            result.Title = productTitleText.Trim();
        }

        _logger.LogWarning("Get Title 2 Done");
        // Circle is company name i guess
        var circleNameElement = document.GetElementsByClassName("contentsDetailTop__tableRow");
        foreach (var rowElement in circleNameElement)
        {
            if (rowElement.GetElementsByTagName("td").FirstOrDefault().Text().Contains("ブランド"))
            {
                result.Circle = rowElement.GetElementsByTagName("td")[1].GetElementsByTagName("a").FirstOrDefault()
                    .Text().Trim();
            }
        }

        _logger.LogWarning("Get Circle Done");

        // Get preview
        var productPreviewElement = document.GetElementsByClassName("slider-area").FirstOrDefault();
        if (productPreviewElement is not null)
        {
            _logger.LogWarning("there is preivew html" +  productPreviewElement.GetElementsByClassName("image-slider").FirstOrDefault()!
                .GetElementsByTagName("img").Length);
            var previewImages =
                productPreviewElement.GetElementsByClassName("image-slider").FirstOrDefault()!
                    .GetElementsByTagName("img").Cast<IHtmlImageElement>().Select(img => img.Source)
                    .Where(source => !string.IsNullOrWhiteSpace(source)).ToList();

            result.PreviewImages = (previewImages.Any() ? previewImages : null)!;
        }

        _logger.LogWarning("Get Preview Done");
        //get review
        var userReviewElement = document.GetElementsByClassName("d-review__average").FirstOrDefault()
            .GetElementsByTagName("strong").FirstOrDefault().Text().Replace("点", "");
        result.Rating = Double.Parse(userReviewElement);


        var description = document.GetElementsByClassName("read-text-area").FirstOrDefault();
        if (description is not null)
        {
            result.Description =  description.InnerHtml;
        }



        var informationListElements = document.GetElementsByClassName("contentsDetailBottom__tableRow");
        if (informationListElements.Any())
        {
            foreach (var informationListElement in informationListElements)
            {
                var ttlElement = informationListElement.GetElementsByClassName("contentsDetailBottom__tableDataLeft")
                    .FirstOrDefault();
                if (ttlElement is null) continue;

                var ttlText = ttlElement.Text().Trim();

                var txtElement = informationListElement.GetElementsByClassName("contentsDetailBottom__tableDataRight")
                    .FirstOrDefault();
                var txt = txtElement?.Text().Trim();

                if (ttlText.Equals("配信開始日", StringComparison.OrdinalIgnoreCase))
                {
                    // release date
                    if (txt is null) continue;

                    // check the txt is format as yyyy/mm/dd via regex

                    // "2021/12/25 00:00"
                    var index = txt.IndexOf(' ');


                    // "2021/12/25"
                    if (index > -1)
                    {
                        txt = txt.Substring(0, index);
                    }

                    if (DateTime.TryParseExact(txt, "yyyy/MM/dd", null, DateTimeStyles.None, out var releaseDate))
                    {
                        result.ReleaseDate = releaseDate;
                    }

                    _logger.LogWarning("release date done" + result.ReleaseDate);
                }
                else if (ttlText.Equals("ゲームジャンル", StringComparison.OrdinalIgnoreCase))
                {
                    // game genres, not the same as genres (this is more like a theme, eg "RPG")
                    if (txt is null) continue;

                    result.GameGenre = txt;
                    _logger.LogWarning("release GameGenre done" + result.GameGenre);
                }
                else if (ttlText.Equals("シリーズ", StringComparison.OrdinalIgnoreCase))
                {
                    // series
                    if (txt is null) continue;
                    if (txt.Equals("----")) continue;

                    result.Series = txt;
                    _logger.LogWarning("release series done" + result.Series);
                }
                else if (ttlText.Equals("ジャンル", StringComparison.OrdinalIgnoreCase))
                {
                    // genres, not the same as game genre (this is more like tags)
                    var genreTagTextElements = informationListElement.GetElementsByClassName(
                        "component-textLink component-textLink__state component-textLink__state--initial");

                    result.Genres = genreTagTextElements
                        .Select(elem => elem.Text().Trim())
                        .ToList();
                    _logger.LogWarning("release gemeres done" + result.Genres.ToString());
                }
            }
        }

        result.IconUrl = string.Format(IconUrlFormat, id);

        return result;
    }

    public async Task<List<SearchResult>> ScrapSearchPage(string term, CancellationToken cancellationToken = default)
    {
        var url = SearchBaseUrl + term;

        var context = BrowsingContext.New(_configuration);
        var document = await context.OpenAsync(url, cancellationToken);

        var anchorElements = document.GetElementsByClassName("tmb")
            .Where(elem => elem.TagName.Equals(TagNames.P, StringComparison.OrdinalIgnoreCase))
            .Select(elem =>
                elem.Children.FirstOrDefault(x => x.TagName.Equals(TagNames.A, StringComparison.OrdinalIgnoreCase)))
            .Cast<IHtmlAnchorElement>();

        var results = new List<SearchResult>();

        foreach (var anchorElement in anchorElements)
        {
            var id = FanzaMetadataProvider.GetIdFromLink(anchorElement.Href);
            if (id is null) continue;

            var txtElement = anchorElement.GetElementsByClassName("txt").FirstOrDefault();
            if (txtElement is null) continue;

            var name = txtElement.Text().Trim();
            var searchResult = new SearchResult(name, id);

            results.Add(searchResult);
        }

        return results;
    }
}
