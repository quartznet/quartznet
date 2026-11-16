using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Readers for the markup shapes the dashboard's shared components render, so a test says what it is
/// about rather than repeating a CSS selector.
/// </summary>
internal static class DashboardMarkup
{
    /// <summary>
    /// The value shown on the <c>StatCard</c> with the given title.
    /// </summary>
    public static string StatCardValue<TComponent>(this IRenderedComponent<TComponent> component, string title)
        where TComponent : IComponent
    {
        foreach (IElement card in component.FindAll(".qz-stat-card"))
        {
            if (string.Equals(card.QuerySelector(".qz-stat-card-title")?.TextContent.Trim(), title, StringComparison.Ordinal))
            {
                return card.QuerySelector(".qz-stat-card-value")?.TextContent.Trim() ?? string.Empty;
            }
        }

        throw new InvalidOperationException($"The page rendered no stat card titled '{title}'.");
    }

    /// <summary>
    /// The text of every element matching <paramref name="selector" />, trimmed.
    /// </summary>
    public static List<string> TextOfAll<TComponent>(this IRenderedComponent<TComponent> component, string selector)
        where TComponent : IComponent
    {
        List<string> texts = [];
        foreach (IElement element in component.FindAll(selector))
        {
            texts.Add(element.TextContent.Trim());
        }

        return texts;
    }
}
