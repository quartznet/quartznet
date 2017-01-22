FROM postgres:latest

ADD tables_postgres.sql /docker-entrypoint-initdb.d/

ENV POSTGRES_USER postgres
ENV POSTGRES_PASSWORD Password12!
ENV POSTGRES_DB quartz
