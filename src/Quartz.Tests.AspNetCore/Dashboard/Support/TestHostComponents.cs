using Microsoft.AspNetCore.Components;

namespace Quartz.Tests.AspNetCore.Dashboard.Support;

/// <summary>
/// Root component standing in for a host application's App component.
/// </summary>
public sealed class TestHostApp : ComponentBase;

/// <summary>
/// A routable host application page outside the dashboard.
/// </summary>
[Route("/host-page")]
public sealed class TestHostPage : ComponentBase;
