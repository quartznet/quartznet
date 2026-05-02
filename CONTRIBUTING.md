# How to contribute

The easiest way to contribute is to open an issue and start a discussion. 
Then we can decide if and how a feature or a change could be implemented and if you should submit a pull requests with code changes.

Also read this first: [Being a good open source citizen](https://hackernoon.com/being-a-good-open-source-citizen-9060d0ab9732#.x3hocgw85)

## General feedback and discussions

Please start a discussion on the [core repo issue tracker](https://github.com/quartznet/quartznet/issues).

## Building

Run `build.cmd` or `build.sh` from the command line. 

## Testing

Integration tests provision their database dependencies through Testcontainers for .NET.

* Ensure your Docker daemon is running
* Run the build command with flags: `.\build.cmd Compile UnitTest IntegrationTest`

This builds and runs tests like the CI server does.

## Bugs and feature requests?

Please log a new issue in the GitHub repo.

## Other discussions

https://gitter.im/quartznet/quartznet and https://groups.google.com/forum/#!forum/quartznet

## Filing issues

Use the issue forms at https://github.com/quartznet/quartznet/issues/new/choose. Each form prompts
for the information we need to investigate, so please fill in every required field.

The fastest path to a fix is a minimal, runnable reproduction in a public GitHub repo we can clone —
isolated repros get triaged first. If your issue is a usage question, please use
[Discussions](https://github.com/quartznet/quartznet/discussions) or the
[`[quartz.net]` tag on Stack Overflow](https://stackoverflow.com/questions/tagged/quartz.net) instead.

Believe you have found a security vulnerability? Do **not** open a public issue. Use
[Security Advisories](https://github.com/quartznet/quartznet/security/advisories/new); see
[`SECURITY.md`](.github/SECURITY.md) for details.

## Contributing code and content

Make sure you can build the code. Familiarize yourself with the project workflow and our coding conventions. If you don't know what a pull request is read this article: https://help.github.com/articles/using-pull-requests.

Before submitting a feature or substantial code contribution please discuss it with the team and ensure it follows the product roadmap. Here's a list of blog posts that are worth reading before doing a pull request:

* [Open Source Contribution Etiquette](http://tirania.org/blog/archive/2010/Dec-31.html) by Miguel de Icaza
* [Don't "Push" Your Pull Requests](http://www.igvita.com/2011/12/19/dont-push-your-pull-requests/) by Ilya Grigorik.
* [10 tips for better Pull Requests](http://blog.ploeh.dk/2015/01/15/10-tips-for-better-pull-requests/) by Mark Seemann
* [How to write the perfect pull request](https://github.com/blog/1943-how-to-write-the-perfect-pull-request) by GitHub

Here's a few things you should always do when making changes to the code base:

**Commit/Pull Request Format**

```
Summary of the changes (Less than 80 chars)
 - Detail 1
 - Detail 2

#bugnumber (in this specific format)
```

**Tests**

-  Tests need to be provided for every bug/feature that is completed.
-  Tests only need to be present for issues that need to be verified by QA (e.g. not tasks).
-  If there is a scenario that is far too hard to test there does not need to be a test for it.
  - "Too hard" is determined by the team as a whole.
