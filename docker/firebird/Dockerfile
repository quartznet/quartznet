FROM jacobalberty/firebird:3.0.6

ENV ISC_PASSWORD masterkey
ENV EnableWireCrypt true

ADD create_database.sql ./create_database.sql
ADD tables_firebird.sql ./tables_firebird.sql

# need to run these manually after attaching to running container
#RUN /usr/local/firebird/bin/isql -q -u SYSDBA -p masterkey -input create_database.sql
#RUN /usr/local/firebird/bin/isql quartz.fdb -q -u SYSDBA -p masterkey -input tables_firebird.sql
