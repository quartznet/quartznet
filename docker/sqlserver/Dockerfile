FROM mcr.microsoft.com/mssql/server:2017-latest

ENV SA_PASSWORD Quartz!DockerP4ss
ENV ACCEPT_EULA Y 
ENV MSSQL_PID Developer

ADD entrypoint.sh .
ADD tables_sqlServer.sql .
ADD import-data.sh .

RUN chmod +x import-data.sh

CMD /bin/bash entrypoint.sh
