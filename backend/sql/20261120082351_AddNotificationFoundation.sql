START TRANSACTION;
CREATE TABLE `NotificationPreferences` (
    `UserId` int NOT NULL,
    `Category` varchar(32) NOT NULL,
    `Enabled` tinyint(1) NOT NULL,
    PRIMARY KEY (`UserId`, `Category`),
    CONSTRAINT `FK_NotificationPreferences_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `Notifications` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `RecipientId` int NOT NULL,
    `ActorId` int NULL,
    `Type` varchar(64) NOT NULL,
    `ResourceType` varchar(32) NULL,
    `ResourceId` bigint NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ReadAt` datetime(6) NULL,
    `EventId` char(36) NOT NULL,
    `PayloadVersion` tinyint unsigned NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Notifications_Users_ActorId` FOREIGN KEY (`ActorId`) REFERENCES `Users` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_Notifications_Users_RecipientId` FOREIGN KEY (`RecipientId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_Notifications_ActorId` ON `Notifications` (`ActorId`);

CREATE INDEX `IX_Notifications_RecipientId_CreatedAt_Id` ON `Notifications` (`RecipientId`, `CreatedAt`, `Id`);

CREATE UNIQUE INDEX `IX_Notifications_RecipientId_EventId_Type` ON `Notifications` (`RecipientId`, `EventId`, `Type`);

CREATE INDEX `IX_Notifications_RecipientId_ReadAt_CreatedAt_Id` ON `Notifications` (`RecipientId`, `ReadAt`, `CreatedAt`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261120082351_AddNotificationFoundation', '10.0.15');

COMMIT;
