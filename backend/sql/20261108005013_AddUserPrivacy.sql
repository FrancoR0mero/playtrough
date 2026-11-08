START TRANSACTION;
ALTER TABLE `Users` ADD `HistoryVisibility` int NOT NULL DEFAULT 1;

ALTER TABLE `Users` ADD `ProfileVisibility` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261108005013_AddUserPrivacy', '10.0.14');

COMMIT;

