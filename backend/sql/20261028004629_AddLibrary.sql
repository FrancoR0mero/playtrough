START TRANSACTION;
CREATE TABLE `LibraryEntries` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `GameId` int NOT NULL,
    `Status` varchar(16) NOT NULL,
    `AddedAt` datetime(6) NOT NULL,
    `StatusChangedOn` date NOT NULL,
    `ConcurrencyToken` char(36) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LibraryEntries_Games_GameId` FOREIGN KEY (`GameId`) REFERENCES `Games` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_LibraryEntries_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `LibraryEvents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `GameId` int NOT NULL,
    `Type` varchar(16) NOT NULL,
    `FromStatus` varchar(16) NULL,
    `ToStatus` varchar(16) NULL,
    `OccurredOn` date NOT NULL,
    `RecordedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LibraryEvents_Games_GameId` FOREIGN KEY (`GameId`) REFERENCES `Games` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_LibraryEvents_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_LibraryEntries_GameId` ON `LibraryEntries` (`GameId`);

CREATE UNIQUE INDEX `IX_LibraryEntries_UserId_GameId` ON `LibraryEntries` (`UserId`, `GameId`);

CREATE INDEX `IX_LibraryEntries_UserId_Status` ON `LibraryEntries` (`UserId`, `Status`);

CREATE INDEX `IX_LibraryEvents_GameId` ON `LibraryEvents` (`GameId`);

CREATE INDEX `IX_LibraryEvents_UserId_GameId` ON `LibraryEvents` (`UserId`, `GameId`);

CREATE INDEX `IX_LibraryEvents_UserId_OccurredOn_Id` ON `LibraryEvents` (`UserId`, `OccurredOn`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261028004629_AddLibrary', '10.0.14');

COMMIT;

