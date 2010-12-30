<%@ Page Language="C#" MasterPageFile="~/Views/Shared/Site.Master" AutoEventWireup="true" CodeBehind="Login.aspx.cs" Inherits="Quartz.Web.Views.Account.Login" %>

<asp:Content ID="loginContent" ContentPlaceHolderID="MainContent" runat="server">
    <h2>Login</h2>
    <p>
        Please enter your username and password below. If you don't have an account,
        please <%= Html.ActionLink("register", "Register") %>.
    </p>
    <%= Html.ValidationSummary() %>

    <% using (Html.BeginForm()) { %>
        <div>
            <table>
                <tr>
                    <td>Username:</td>
                    <td>
                        <%= Html.TextBox("username") %>
                        <%= Html.ValidationMessage("username") %>
                    </td>
                </tr>
                <tr>
                    <td>Password:</td>
                    <td>
                        <%= Html.Password("password") %>
                        <%= Html.ValidationMessage("password") %>
                    </td>
                </tr>
                <tr>
                    <td></td>
                    <td><%= Html.CheckBox("rememberMe") %> Remember me?</td>
                </tr>
                <tr>
                    <td></td>
                    <td><input type="submit" value="Login" /></td>
                </tr>
            </table>
        </div>
    <% } %>
</asp:Content>
