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
        return StatCard(component, title).QuerySelector(".qz-stat-card-value")?.TextContent.Trim() ?? string.Empty;
    }

    /// <summary>
    /// The <c>href</c> of the link wrapping the <c>StatCard</c> with the given title, or
    /// <see langword="null" /> when that tile is not a link. Titled rather than positional, because
    /// several tiles are links and a bare selector would silently read whichever came first.
    /// </summary>
    public static string? StatCardLinkHref<TComponent>(this IRenderedComponent<TComponent> component, string title)
        where TComponent : IComponent
    {
        return StatCard(component, title).Closest(".qz-stat-card-link")?.GetAttribute("href");
    }

    /// <summary>
    /// The classes on the <c>StatCard</c> with the given title, which carry its colour.
    /// </summary>
    public static ITokenList StatCardClasses<TComponent>(this IRenderedComponent<TComponent> component, string title)
        where TComponent : IComponent
    {
        return StatCard(component, title).ClassList;
    }

    private static IElement StatCard<TComponent>(IRenderedComponent<TComponent> component, string title)
        where TComponent : IComponent
    {
        foreach (IElement card in component.FindAll(".qz-stat-card"))
        {
            if (string.Equals(card.QuerySelector(".qz-stat-card-title")?.TextContent.Trim(), title, StringComparison.Ordinal))
            {
                return card;
            }
        }

        throw new InvalidOperationException($"The page rendered no stat card titled '{title}'.");
    }

    /// <summary>
    /// The CSS modifier the one status→class mapping in the dashboard produced, for example
    /// <c>qz-state-running</c>.
    /// </summary>
    public static string SchedulerStatusModifier<TComponent>(this IRenderedComponent<TComponent> component)
        where TComponent : IComponent
    {
        IElement dot = component.Find(".qz-state-indicator .qz-state-dot");
        foreach (string token in dot.ClassList)
        {
            if (token is not "qz-state-dot" and not "qz-state-dot-lg")
            {
                return token;
            }
        }

        throw new InvalidOperationException("The status dot carried no status modifier class.");
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

    /// <summary>
    /// Whether the page renders a button with the given label.
    /// </summary>
    public static bool HasButton<TComponent>(this IRenderedComponent<TComponent> component, string label)
        where TComponent : IComponent
    {
        foreach (IElement button in component.FindAll("button"))
        {
            if (string.Equals(button.TextContent.Trim(), label, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
