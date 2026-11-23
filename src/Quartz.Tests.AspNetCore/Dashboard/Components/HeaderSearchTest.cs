#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Components.Layout;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The header's search (#4411): a job or a trigger by name from any page, in two reads of eight, debounced,
/// from two characters, and a keystroke cancels the read in flight.
/// </summary>
public class HeaderSearchTest
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(30);

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        GivenJobs();
        GivenTriggers();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ANameFindsItsJobsAndTriggersAndLinksToEach()
    {
        GivenJobs(total: 11, new JobKeyDto("billing", "invoice-export"));
        GivenTriggers(total: 1, new TriggerHeaderDto("billing", "invoice-nightly", "Cron", null, TriggerState.Normal, null));

        IRenderedComponent<HeaderSearch> search = Render();
        search.Find("input").Input("invoice");

        search.WaitForAssertion(() => search.Find("[data-testid=header-search-jobs] a").GetAttribute("href").Should().Be("quartz/jobs/billing/invoice-export"));
        search.Find("[data-testid=header-search-triggers] a").GetAttribute("href").Should().Be("quartz/triggers/billing/invoice-nightly");
        search.FindAll("[data-testid=header-search-results] a.qz-header-search-see-all").Select(link => (link.TextContent, link.GetAttribute("href"))).Should().Equal([
            ("See all 11 jobs", "quartz/jobs?name=invoice"),
            ("Open the triggers listing", "quartz/triggers?name=invoice"),
        ]);

        A.CallTo(() => context.Api.QueryJobs(TestData.SchedulerName, A<DashboardJobQuery>.That.Matches(query => query.NameContains == "invoice" && query.Take == 8), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>.That.Matches(query => query.NameContains == "invoice" && query.Take == 8), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The box is an ARIA combobox: it says whether its list is open and names the list only while there is one,
    /// and the list is a listbox of options.
    /// </summary>
    [Test]
    public void TheBoxIsAComboboxThatNamesItsListWhileOpen()
    {
        GivenOneOfEach();
        IRenderedComponent<HeaderSearch> search = Render();
        IElement input = search.Find("input");

        input.GetAttribute("role").Should().Be("combobox");
        input.GetAttribute("aria-expanded").Should().Be("false");
        input.HasAttribute("aria-controls").Should().BeFalse("nothing it could control exists while the list is closed");

        input.Input("invoice");

        search.WaitForAssertion(() => search.Find("input").GetAttribute("aria-expanded").Should().Be("true"));
        string controls = search.Find("input").GetAttribute("aria-controls")!;
        search.Find("#" + controls).GetAttribute("role").Should().Be("listbox");
        search.FindAll("[role=option]").Select(option => option.TextContent).Should().Equal(
            ["billing.invoice-export", "Open the jobs listing", "billing.invoice-nightly", "Open the triggers listing"]);
        search.FindAll("[role=option]").Should().OnlyContain(option => option.GetAttribute("tabindex") == "-1",
            "focus stays in the box; the options are reached with the arrow keys");
    }

    /// <summary>
    /// The arrow keys move a highlight through the results, wrapping at either end, and the box names the
    /// highlighted one with aria-activedescendant.
    /// </summary>
    [Test]
    public async Task TheArrowKeysMoveTheHighlight()
    {
        GivenOneOfEach();
        IRenderedComponent<HeaderSearch> search = Render();
        search.Find("input").Input("invoice");
        search.WaitForAssertion(() => search.FindAll("[role=option]").Should().HaveCount(4));

        await Press(search, "ArrowDown");
        Highlighted(search).Should().Be("billing.invoice-export");
        search.Find("input").GetAttribute("aria-activedescendant").Should().Be(search.Find("[aria-selected=true]").Id);

        await Press(search, "ArrowDown");
        await Press(search, "ArrowDown");
        Highlighted(search).Should().Be("billing.invoice-nightly");

        await Press(search, "ArrowUp");
        Highlighted(search).Should().Be("Open the jobs listing");

        await Press(search, "ArrowUp");
        await Press(search, "ArrowUp");
        Highlighted(search).Should().Be("Open the triggers listing", "up from the first wraps to the last");
        search.FindAll(".qz-header-search-option-active").Should().ContainSingle("the highlight is visible as well as announced");
    }

    [Test]
    public async Task EnterOpensTheHighlightedResult()
    {
        GivenOneOfEach();
        IRenderedComponent<HeaderSearch> search = Render();
        search.Find("input").Input("invoice");
        search.WaitForAssertion(() => search.FindAll("[role=option]").Should().HaveCount(4));

        await Press(search, "ArrowDown");
        await Press(search, "ArrowDown");
        await Press(search, "ArrowDown");
        await Press(search, "Enter");

        context.CurrentUri.Should().EndWith("quartz/triggers/billing/invoice-nightly");
        search.WaitForAssertion(() => search.FindAll("[data-testid=header-search-results]").Should().BeEmpty());
    }

    /// <summary>
    /// Escape closes the list, from the box or from the list itself, keeps the text, and puts focus back in the
    /// box; the arrow keys open it again.
    /// </summary>
    [Test]
    public async Task EscapeClosesTheListAndReturnsFocusToTheBox()
    {
        GivenOneOfEach();
        IRenderedComponent<HeaderSearch> search = Render();
        search.Find("input").Input("invoice");
        search.WaitForAssertion(() => search.FindAll("[role=option]").Should().HaveCount(4));

        await search.Find("[data-testid=header-search-results]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        search.FindAll("[data-testid=header-search-results]").Should().BeEmpty();
        IElement input = search.Find("input");
        input.GetAttribute("aria-expanded").Should().Be("false");
        input.HasAttribute("aria-controls").Should().BeFalse();
        input.HasAttribute("aria-activedescendant").Should().BeFalse();
        input.GetAttribute("value").Should().Be("invoice", "Escape closes the list, not the search");
        context.JSInterop.VerifyFocusAsyncInvoke();

        await Press(search, "ArrowDown");
        Highlighted(search).Should().Be("billing.invoice-export", "the arrow keys open what Escape closed");
    }

    [Test]
    public async Task LeavingTheBoxClosesTheList()
    {
        GivenOneOfEach();
        IRenderedComponent<HeaderSearch> search = Render();
        search.Find("input").Input("invoice");
        search.WaitForAssertion(() => search.FindAll("[role=option]").Should().HaveCount(4));

        await search.Find("input").FocusOutAsync(new FocusEventArgs());

        search.FindAll("[data-testid=header-search-results]").Should().BeEmpty();
        search.Find("input").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Test]
    public void OneCharacterReadsNothing()
    {
        IRenderedComponent<HeaderSearch> search = Render();

        search.Find("input").Input("i");
        search.Find("input").Input(" i ");

        A.CallTo(() => context.Api.QueryJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
        search.FindAll("[data-testid=header-search-results]").Should().BeEmpty();
    }

    [Test]
    public void AWordReadsOnceNotOncePerLetter()
    {
        IRenderedComponent<HeaderSearch> search = Render(TimeSpan.FromMilliseconds(200));
        IElement input = search.Find("input");

        input.Input("in");
        input.Input("inv");
        input.Input("invo");

        search.WaitForAssertion(() => search.Find("[data-testid=header-search-none]").TextContent.Should().Contain("'invo'"));
        A.CallTo(() => context.Api.QueryJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Over a slow target the read of an earlier word may still be out when the next one is typed: it is
    /// cancelled, and only the last word's answer is shown.
    /// </summary>
    [Test]
    public void AKeystrokeCancelsTheReadInFlight()
    {
        TaskCompletionSource<CancellationToken> asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GivenJobs(total: 1, new JobKeyDto("billing", "invoice-export"));
        A.CallTo(() => context.Api.QueryJobs(TestData.SchedulerName, A<DashboardJobQuery>.That.Matches(query => query.NameContains == "inv"), A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardJobQuery _, CancellationToken cancellationToken) =>
            {
                asked.TrySetResult(cancellationToken);
                return new ValueTask<PagedResult<JobKeyDto>>(Never<PagedResult<JobKeyDto>>(cancellationToken));
            });

        IRenderedComponent<HeaderSearch> search = Render();
        IElement input = search.Find("input");
        input.Input("inv");
        asked.Task.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the read for 'inv' is made once typing pauses");
        CancellationToken inFlight = asked.Task.Result;

        input.Input("invo");

        // Checked once the keystroke has been handled: its event may wait behind the first read's continuation.
        search.WaitForAssertion(() => inFlight.IsCancellationRequested.Should().BeTrue("the read for 'inv' answers a word nobody is looking for"));
        search.WaitForAssertion(() => search.Find("[data-testid=header-search-jobs]").TextContent.Should().Contain("billing.invoice-export"));
    }

    [Test]
    public void FollowingAResultClosesTheDropdown()
    {
        GivenJobs(total: 1, new JobKeyDto("billing", "invoice-export"));
        IRenderedComponent<HeaderSearch> search = Render();
        search.Find("input").Input("invoice");
        search.WaitForAssertion(() => search.Find("[data-testid=header-search-results]"));

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/quartz/jobs/billing/invoice-export");

        search.WaitForAssertion(() => search.FindAll("[data-testid=header-search-results]").Should().BeEmpty());
        search.Find("input").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Test]
    public void TheHeaderCarriesTheSearch()
    {
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddContent(0, "page")));

        layout.FindAll(".qz-header-left [role=search] input#qz-header-search").Should().ContainSingle();
    }

    private void GivenOneOfEach()
    {
        GivenJobs(total: 1, new JobKeyDto("billing", "invoice-export"));
        GivenTriggers(total: 1, new TriggerHeaderDto("billing", "invoice-nightly", "Cron", null, TriggerState.Normal, null));
    }

    /// <summary>
    /// Presses a key in the box and waits for it to be handled: the renderer may still be finishing the debounced
    /// read when the test goes on, and a key sent without waiting would be handled after the next look.
    /// </summary>
    private static Task Press(IRenderedComponent<HeaderSearch> search, string key)
    {
        return search.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = key });
    }

    private static string Highlighted(IRenderedComponent<HeaderSearch> search)
    {
        return search.Find("[role=option][aria-selected=true]").TextContent;
    }

    private IRenderedComponent<HeaderSearch> Render(TimeSpan? debounce = null)
    {
        return context.Render<HeaderSearch>(parameters => parameters.Add(x => x.Debounce, debounce ?? Debounce));
    }

    private void GivenJobs(int total = 0, params JobKeyDto[] jobs)
    {
        A.CallTo(() => context.Api.QueryJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobKeyDto>([.. jobs], HasMore: total > jobs.Length, TotalCount: total));
    }

    private void GivenTriggers(int total = 0, params TriggerHeaderDto[] triggers)
    {
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([.. triggers], HasMore: total > triggers.Length, TotalCount: total));
    }

    private static async Task<T> Never<T>(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }
}