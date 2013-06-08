--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--
-- Migration script to add new column to QRTZ_FIRED_TRIGGERS
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--

-- you may need to change this syntax depending on your database!

-- common
-- delete from QRTZ_FIRED_TRIGGERS 
alter table QRTZ_FIRED_TRIGGERS add SCHED_TIME [BIGINT] NOT NULL;

-- mysql
-- alter table QRTZ_FIRED_TRIGGERS add SCHED_TIME BIGINT(19) NOT NULL

-- oracle
-- alter table QRTZ_FIRED_TRIGGERS add SCHED_TIME NUMBER(19) NOT NULL

