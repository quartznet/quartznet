---
title: Quartz Enterprise Scheduler .NET
layout: default
---


# Welcome to the home of Quartz.NET!
	
<cite>
Quartz.NET is a full-featured, open source job scheduling system that can be used from smallest apps to large scale enterprise systems.
</cite>

Quartz.NET is a pure .NET library written in C# and is a port of very popular open source Java job scheduling framework, <a href="http://www.quartz-scheduler.org">Quartz</a>. This project owes very much to original Java project, it's father James House and the project contributors. 

{% for post in site.posts limit:8 %}

<h2><a href="{{post.url}}">{{post.title}}</a></h2>

<div class="descr">{{ post.date | date: "%-d %B %Y" }} by {{site.author}}</div>

{{post.content}}
	
{% endfor %}


Older news can be found from [the news archive &raquo;](/old_news.html).