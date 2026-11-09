using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace SystematiaBot.Infrastructure.Jobs;

/// <summary>HTML → readable plain text: one line per paragraph, list item and &lt;br&gt;, list items as "• ".</summary>
public static class HtmlText
{
    public static string FromHtml(string html)
    {
        var document = new HtmlParser().ParseDocument($"<body>{html}</body>");
        return FromElement(document.Body!);
    }

    public static string FromElement(IElement root)
    {
        var sb = new StringBuilder();
        Walk(root, sb);
        var lines = sb.ToString().Split('\n').Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        return string.Join('\n', lines.Where(l => l.Length > 0));
    }

    private static void Walk(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    sb.Append(text.Data);
                    break;
                case IElement { TagName: "BR" }:
                    sb.Append('\n');
                    break;
                case IElement { TagName: "SCRIPT" or "STYLE" }:
                    break;
                case IElement element:
                    var block = element.TagName is "P" or "DIV" or "LI" or "UL" or "OL" or "H1" or "H2" or "H3" or "H4" or "TR";
                    if (block)
                        sb.Append('\n');
                    if (element.TagName == "LI")
                        sb.Append("• ");
                    Walk(element, sb);
                    if (block)
                        sb.Append('\n');
                    break;
            }
        }
    }
}
