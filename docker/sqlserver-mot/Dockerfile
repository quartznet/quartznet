FROM microsoft/mssql-server-linux:2017-latest

ENV SA_PASSWORD Quartz!DockerP4ss
ENV ACCEPT_EULA Y 
ENV MSSQL_PID Developer

ADD entrypoint.sh .
ADD tables_sqlServerMOT.sql .
ADD import-data.sh .

RUN chmod +x import-data.sh

CMD /bin/bash entrypoint.sh
