FROM mysql:latest

ADD tables_mysql_innodb.sql /docker-entrypoint-initdb.d/

ENV MYSQL_DATABASE quartz
ENV MYSQL_ROOT_PASSWORD Password12!
