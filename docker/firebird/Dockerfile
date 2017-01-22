FROM jacobalberty/firebird

ADD create_database.sql ./create_database.sql
ADD tables_firebird.sql ./tables_firebird.sql

RUN /usr/local/firebird/bin/isql -q -u SYSDBA -p masterkey -input create_database.sql
RUN /usr/local/firebird/bin/isql quartz.fdb -q -u SYSDBA -p masterkey -input tables_firebird.sql
