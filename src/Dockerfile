FROM microsoft/dotnet:latest

RUN mkdir /src
ADD Quartz /src/Quartz
ADD Quartz.Tests.Unit /src/Quartz.Tests.Unit
ADD Quartz.Tests.Integration /src/Quartz.Tests.Integration

WORKDIR /src/Quartz
RUN dotnet restore
RUN dotnet build -f netstandard1.5

WORKDIR /src/Quartz.Tests.Unit
RUN dotnet restore
RUN dotnet build -f netcoreapp1.0
RUN dotnet run
