using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace TransLog.Web.Fleet;

/// <summary>Draws one expiry line for the lists: "Disc 2026-12-01 [Due in 4 days]".</summary>
public static class ExpiryHtml
{
    /// <summary>Empty when there is no date. Everything written out is encoded.</summary>
    public static IHtmlContent Line(string label, DateOnly? date, DateOnly today)
    {
        if (date is null)
            return HtmlString.Empty;

        var info = Expiry.Describe(date, today);
        var html = new StringBuilder();
        html.Append("<div class=\"small\"><span class=\"text-body-secondary\">")
            .Append(WebUtility.HtmlEncode(label))
            .Append("</span> ")
            .Append(WebUtility.HtmlEncode(Expiry.Format(date)));
        if (info.BadgeCss.Length > 0)
        {
            html.Append(" <span class=\"badge ").Append(info.BadgeCss).Append("\">")
                .Append(WebUtility.HtmlEncode(info.Text)).Append("</span>");
        }
        html.Append("</div>");
        return new HtmlString(html.ToString());
    }
}
