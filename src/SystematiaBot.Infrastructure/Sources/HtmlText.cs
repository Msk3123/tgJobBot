using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace SystematiaBot.Infrastructure.Sources;

internal static class HtmlText
{
    private static readonly HashSet<string> BlockTags = ["P", "DIV", "LI", "H1", "H2", "H3", "H4", "BLOCKQUOTE", "TR"];

    /// <summary>Plain text of an element keeping line breaks for &lt;br&gt; and paragraph breaks for block elements.</summary>
    public static string ToText(INode node)
    {
        var sb = new StringBuilder();
        Append(node, sb);
        var lines = sb.ToString().Replace("\r", "").Split('\n').Select(l => string.Join(' ', l.Split(' ', '\t', ' ').Where(w => w.Length > 0)));
        return CollapseBlankLines(string.Join('\n', lines)).Trim();
    }

    public static string FromHtml(string html)
    {
        using var doc = new HtmlParser().ParseDocument($"<body>{html}</body>");
        return ToText(doc.Body!);
    }

    private static void Append(INode node, StringBuilder sb)
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
                case IElement { TagName: "SCRIPT" or "STYLE" or "NOSCRIPT" }:
                    break;
                case IElement element:
                    var block = BlockTags.Contains(element.TagName);
                    if (block)
                        sb.Append("\n\n");
                    Append(element, sb);
                    if (block)
                        sb.Append("\n\n");
                    break;
            }
        }
    }

    private static string CollapseBlankLines(string text)
    {
        while (text.Contains("\n\n\n"))
            text = text.Replace("\n\n\n", "\n\n");
        return text;
    }
}
