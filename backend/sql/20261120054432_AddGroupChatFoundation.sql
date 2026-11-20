START TRANSACTION;
ALTER TABLE `OutboxMessages` ADD `Attempts` int NOT NULL DEFAULT 0;

ALTER TABLE `OutboxMessages` ADD `FailedAt` datetime(6) NULL;

ALTER TABLE `OutboxMessages` ADD `LeaseExpiresAt` datetime(6) NULL;

ALTER TABLE `OutboxMessages` ADD `LeaseId` char(36) NULL;

ALTER TABLE `OutboxMessages` ADD `NextAttemptAt` datetime(6) NULL;

ALTER TABLE `OutboxMessages` ADD `ProcessedAt` datetime(6) NULL;

CREATE TABLE `GroupMessages` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `GroupId` int NOT NULL,
    `AuthorId` int NOT NULL,
    `ClientMessageId` char(36) NOT NULL,
    `Text` varchar(2000) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GroupMessages_SocialGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `SocialGroups` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_GroupMessages_Users_AuthorId` FOREIGN KEY (`AuthorId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_OutboxMessages_Type_ProcessedAt_FailedAt_NextAttemptAt` ON `OutboxMessages` (`Type`, `ProcessedAt`, `FailedAt`, `NextAttemptAt`);

CREATE UNIQUE INDEX `IX_GroupMessages_AuthorId_ClientMessageId` ON `GroupMessages` (`AuthorId`, `ClientMessageId`);

CREATE INDEX `IX_GroupMessages_GroupId_CreatedAt_Id` ON `GroupMessages` (`GroupId`, `CreatedAt`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261120054432_AddGroupChatFoundation', '10.0.15');

COMMIT;
