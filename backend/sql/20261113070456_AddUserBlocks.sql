START TRANSACTION;
CREATE TABLE `UserBlocks` (
    `BlockerId` int NOT NULL,
    `BlockedId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`BlockerId`, `BlockedId`),
    CONSTRAINT `CK_UserBlocks_DifferentUsers` CHECK (`BlockerId` <> `BlockedId`),
    CONSTRAINT `FK_UserBlocks_Users_BlockedId` FOREIGN KEY (`BlockedId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_UserBlocks_Users_BlockerId` FOREIGN KEY (`BlockerId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_UserBlocks_BlockedId_BlockerId` ON `UserBlocks` (`BlockedId`, `BlockerId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261113070456_AddUserBlocks', '10.0.15');

COMMIT;
