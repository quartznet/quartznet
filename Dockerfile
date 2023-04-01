FROM microsoft/dotnet:latest

RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF

RUN echo "deb http://download.mono-project.com/repo/debian wheezy-libjpeg62-compat main" > /etc/apt/sources.list.d/mono-xamarin.list \
  && echo "deb http://download.mono-project.com/repo/debian wheezy main" >> /etc/apt/sources.list.d/mono-xamarin.list \
  && apt-get update \
  && apt-get install -y binutils mono-devel nuget referenceassemblies-pcl \
  && rm -rf /var/lib/apt/lists/* /tmp/*

WORKDIR /app
ADD src/Quartz app/src/Quartz
ADD src/Quartz.Serialization.Json app/src/Quartz.Serialization.Json
ADD src/Quartz.Examples app/src/Quartz.Examples
ADD src/Quartz.Tests.Unit app/src/Quartz.Tests.Unit
ADD src/Quartz.Tests.Integration app/src/Quartz.Tests.Integration

COPY tools/NuGet app/tools/NuGet
COPY ./*.* app/

RUN chmod a+x ./build.sh

# run default units tests only
RUN ./build.sh Test
