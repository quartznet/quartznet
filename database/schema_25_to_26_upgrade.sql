/*
Upgrade Quartz.NET schema for SQL Server database (or other database in commented code)
Migration from 2.5 to 2.6
*/
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--
-- Migration script to add new column to QRTZ_SIMPROP_TRIGGERS
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--

-- you may need to change this syntax depending on your database!

-- sql server

IF COL_LENGTH('QRTZ_SIMPROP_TRIGGERS','TIME_ZONE_ID') IS NULL
BEGIN
  alter table [QRTZ_SIMPROP_TRIGGERS] add TIME_ZONE_ID [NVARCHAR] (80);
END

IF COL_LENGTH('QRTZ_CRON_TRIGGERS','TIME_ZONE_ID') IS NULL
BEGIN
  alter table [QRTZ_CRON_TRIGGERS] add TIME_ZONE_ID [NVARCHAR] (80);
END

-- mysql
-- alter table QRTZ_SIMPROP_TRIGGERS add TIME_ZONE_ID VARCHAR(80);
-- alter table QRTZ_CRON_TRIGGERS add TIME_ZONE_ID VARCHAR(80);

-- oracle
-- alter table QRTZ_SIMPROP_TRIGGERS add TIME_ZONE_ID VARCHAR2(80);
-- alter table QRTZ_CRON_TRIGGERS add TIME_ZONE_ID VARCHAR2(80);
