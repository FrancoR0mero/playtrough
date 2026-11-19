START TRANSACTION;
CREATE TABLE `SocialGroups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OwnerId` int NOT NULL,
    `Name` varchar(80) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `Topic` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `Privacy` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `DeletedAt` datetime(6) NULL,
    `ConcurrencyToken` char(36) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SocialGroups_Users_OwnerId` FOREIGN KEY (`OwnerId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `GroupMembers` (
    `GroupId` int NOT NULL,
    `UserId` int NOT NULL,
    `JoinedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`GroupId`, `UserId`),
    CONSTRAINT `FK_GroupMembers_SocialGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `SocialGroups` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GroupMembers_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_GroupMembers_UserId` ON `GroupMembers` (`UserId`);

CREATE INDEX `IX_SocialGroups_DeletedAt_Id` ON `SocialGroups` (`DeletedAt`, `Id`);

CREATE INDEX `IX_SocialGroups_DeletedAt_Topic_Id` ON `SocialGroups` (`DeletedAt`, `Topic`, `Id`);

CREATE INDEX `IX_SocialGroups_OwnerId` ON `SocialGroups` (`OwnerId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261119211317_AddSocialGroups', '10.0.15');

COMMIT;
