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
