<%@ Page Language="C#" MasterPageFile="~/Views/Shared/Site.Master" AutoEventWireup="true" Inherits="Quartz.Web.Views.BaseViewPage<object>" %>
<%@ Import Namespace="Quartz.Web.Localization"%>


<asp:Content ID="indexContent" ContentPlaceHolderID="MainContent" runat="server">


<%=Html.BeginForm()%>
<input type="hidden" id="command" name="command" value=""/>
<table>
	<tr>
		<td width="200">label.scheduler</td>
		<td>
    		<%= Html.DropDownList("schedulerName", new SelectList((IEnumerable) ViewData["Schedulers"], "SchedulerName", "SchedulerName")) %>
		</td>
	</tr>
<%
    var metaData = (Quartz.SchedulerMetaData)ViewData["SchedulerMetaData"];
    var scheduler = (Quartz.IScheduler)ViewData["ActiveScheduler"];

    if (scheduler != null)
    {
%>
	    <tr>
		    <td><%= Html.Resource("label.scheduler.schedulerName") %></td><td><%= metaData.SchedulerName %></td>
	    </tr>
	    <tr> 
		    <td>label.scheduler.state</td><td><%= scheduler %>"/></td>
	    </tr>
	    <tr>
		    <td><@s.text name="label.scheduler.runningSince"/></td><td><%= metaData.RunningSince%></td>
	    </tr>
	    <tr>
		    <td><@s.text name="label.scheduler.numJobsExecuted"/></td><td><%= metaData.NumJobsExecuted%></td>
	    </tr>
	    <tr>
		    <td><@s.text name="label.scheduler.persistenceType"/></td><td><%= metaData.JobStoreType %> </td>
	    </tr>
	    <tr>
		    <td><@s.text name="label.scheduler.threadPoolSize"/></td><td><%= metaData.ThreadPoolSize%></td>
	    </tr>
	    <tr>
		    <td><@s.text name="label.scheduler.version"/></td><td><%= metaData.Version%></td>
	    </tr>
<%
    }
%>
</table>	
	<span id="controls">
	    <%= Html.ActionLink("hint.scheduler.start", "Start") %>
	    <%= Html.ActionLink("hint.scheduler.pause", "Pause") %>
	    <%= Html.ActionLink("hint.scheduler.stop", "Stop") %>
	    <%= Html.ActionLink("hint.scheduler.waitAndStop", "WaitAndStop")%>
	</span>
	<br/>
	
title.chooseScheduler.setCurrentScheduler: <%= Html.ActionLink("btnSetSchedulerAsCurrent", "set") %>
</form>
<hr/>
title.chooseScheduler.executingJobs
<table><tr>
<td>label.job.group</td>
<td>label.job.name</td>
<td>label.job.description</td>
<td>label.job.jobClass</td>
</tr>

<%
    if (ViewData["CurrentlyExecutingJobs"] != null) 
    {
        foreach (Quartz.JobExecutionContext job in (IEnumerable)ViewData["CurrentlyExecutingJobs"])
        {
%>
	<tr>
		<td><%= job.JobDetail.Group %></td>
		<td><%= job.JobDetail.Name%></td>
		<td><%= job.JobDetail.Description%></td>
		<td><%= job.JobDetail.JobType%></td>
	</tr>

<%
        }
%>
    
<%
    }
%>
</table>
<table>
	<tr>
		<td width="30">
			<img src="${base}/icons/Pause24.gif" value="btnPauseAllJobs" alt="Pause all jobs"/>
		</td>
		<td width="30">
			<img src="${base}/icons/Play24.gif" value="btnResumeAllJobs" alt="Resume all jobs"/>
		</td>
	</tr>
</table>

<hr/>
<p>label.scheduler.summary: <i><pre><%= metaData.GetSummary() %></pre></i></p>


</asp:Content>
