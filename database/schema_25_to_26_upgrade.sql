--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--
-- Migration script to add new column to QRTZ_SIMPROP_TRIGGERS
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--

-- you may need to change this syntax depending on your database!

-- sql server
alter table QRTZ_SIMPROP_TRIGGERS add TIME_ZONE_ID [NVARCHAR] (80);

-- mysql
-- alter table QRTZ_SIMPROP_TRIGGERS add TIME_ZONE_ID VARCHAR(80);

-- oracle
-- alter table QRTZ_SIMPROP_TRIGGERS add TIME_ZONE_ID VARCHAR2(80);
