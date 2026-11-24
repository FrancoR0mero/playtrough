START TRANSACTION;
CREATE INDEX `IX_GroupMessages_GroupId_Id` ON `GroupMessages` (`GroupId`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261120060856_IndexGroupChatByMessageId', '10.0.15');

COMMIT;
