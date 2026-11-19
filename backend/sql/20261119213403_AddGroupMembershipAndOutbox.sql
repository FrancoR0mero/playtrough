START TRANSACTION;
ALTER TABLE `GroupMembers` ADD `RemovedAt` datetime(6) NULL;

CREATE TABLE `GroupJoinRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `GroupId` int NOT NULL,
    `UserId` int NOT NULL,
    `Status` int NOT NULL,
    `PendingUserId` int AS (CASE WHEN `Status` = 0 THEN `UserId` ELSE NULL END) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ResolvedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GroupJoinRequests_SocialGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `SocialGroups` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GroupJoinRequests_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `OutboxMessages` (
    `Id` char(36) NOT NULL,
    `Type` varchar(100) NOT NULL,
    `AggregateId` varchar(100) NOT NULL,
    `Payload` text CHARACTER SET utf8mb4 NOT NULL,
    `OccurredAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE UNIQUE INDEX `IX_GroupJoinRequests_GroupId_PendingUserId` ON `GroupJoinRequests` (`GroupId`, `PendingUserId`);

CREATE INDEX `IX_GroupJoinRequests_GroupId_Status_Id` ON `GroupJoinRequests` (`GroupId`, `Status`, `Id`);

CREATE INDEX `IX_GroupJoinRequests_GroupId_UserId_Id` ON `GroupJoinRequests` (`GroupId`, `UserId`, `Id`);

CREATE INDEX `IX_GroupJoinRequests_UserId` ON `GroupJoinRequests` (`UserId`);

CREATE INDEX `IX_OutboxMessages_OccurredAt_Id` ON `OutboxMessages` (`OccurredAt`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261119213403_AddGroupMembershipAndOutbox', '10.0.15');

COMMIT;
