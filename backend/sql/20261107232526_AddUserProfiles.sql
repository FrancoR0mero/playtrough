START TRANSACTION;
ALTER TABLE `Users` ADD `Bio` varchar(300) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Users` ADD `UsernameChangedAt` datetime(6) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261107232526_AddUserProfiles', '10.0.14');

COMMIT;

