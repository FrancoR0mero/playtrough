START TRANSACTION;
ALTER TABLE `GroupMessages` ADD `RemovedAt` datetime(6) NULL;

ALTER TABLE `GroupMessages` ADD `RemovedByUserId` int NULL;

CREATE INDEX `IX_GroupMessages_RemovedByUserId` ON `GroupMessages` (`RemovedByUserId`);

ALTER TABLE `GroupMessages` ADD CONSTRAINT `FK_GroupMessages_Users_RemovedByUserId` FOREIGN KEY (`RemovedByUserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261120062802_AddGroupChatMessageRemoval', '10.0.15');

COMMIT;
