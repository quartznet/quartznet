<%@ Page Language="C#" MasterPageFile="~/Views/Shared/Site.Master" AutoEventWireup="true" Inherits="System.Web.Mvc.ViewPage" %>

<asp:Content ID="indexContent" ContentPlaceHolderID="MainContent" runat="server">
 <div id="middlebox">
<div class="heading primary">Welcome to the Quartz web application. </div>
<p>This application provides a
sample implement ion of quartz within a servlet environment. For
additional documentation regarding quartz, refer to

</p>
<div class="heading primary expanded"><b>Menu Overview</b></div>
<div class="source" bgcolor="#eeeeee">
<div class="secondary subheading">Chooser Scheduler</div>

<p>
Provides a way to choose scheduler provide in quartz.properties and
start/stop/pause operations.
</p>

<div class="secondary subheading">Job Definitions</div>

<p>
Job Definitions provide a way to templating job setup characteristics.
For instance, by supplying a definition for quartz's NativeJob, a user
does not have to remember the class for running native executables is
actually org.quartz.jobs.NativeJob. In addition, a definition provides a
way of specifying the required/optional parameters for a job. 

e.g. (command, and parameters string for NativeJob).
</p>

<div class="secondary subheading">Create Job</div>
<p>
Create "raw job". Used to create a job with the aid of a job definition.
After a job is created it may be schedule to run.
</p>

<div class="secondary subheading">List all jobs</div>
<p>

Lists all jobs currently defined in the system. Jobs may be selected and
schedule from this view. 
</p>

<div class="secondary subheading">Lists all Triggers</div>
<p>Lists all triggers for jobs that have been scheduled to execute. </p>
</div>
</div>


</asp:Content>
