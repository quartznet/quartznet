FROM microsoft/dotnet:latest

RUN apt-get update \
  && apt-get install -y curl \
  && rm -rf /var/lib/apt/lists/*

RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF

RUN echo "deb http://download.mono-project.com/repo/debian wheezy-libjpeg62-compat main" > /etc/apt/sources.list.d/mono-xamarin.list \
  && echo "deb http://download.mono-project.com/repo/debian wheezy/snapshots/4.4.1.0 main" >> /etc/apt/sources.list.d/mono-xamarin.list \
  && apt-get update \
  && apt-get install -y binutils mono-devel nuget referenceassemblies-pcl \
  && rm -rf /var/lib/apt/lists/* /tmp/*

RUN mkdir /src
ADD src/Quartz app/src/Quartz
ADD src/Quartz.Tests.Unit app/src/Quartz.Tests.Unit
ADD src/Quartz.Tests.Integration app/src/Quartz.Tests.Integration

ADD tools/NuGet app/tools/NuGet
ADD ./*.* app/

WORKDIR app
RUN mono tools/NuGet/NuGet.exe install FAKE -Version 4.36.0 -OutputDirectory packages -ExcludeVersion
RUN mono packages/FAKE/tools/FAKE.exe build.fsx $@
