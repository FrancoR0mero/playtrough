START TRANSACTION;
CREATE TABLE `TimelineEntries` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `Type` varchar(16) NOT NULL,
    `GameId` int NOT NULL,
    `FromStatus` varchar(16) NULL,
    `ToStatus` varchar(16) NULL,
    `OccurredOn` date NOT NULL,
    `RecordedAt` datetime(6) NOT NULL,
    `LibraryEventId` int NULL,
    `ReviewId` int NULL,
    `IsHidden` tinyint(1) NOT NULL DEFAULT FALSE,
    `FeedEligible` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_TimelineEntries_Games_GameId` FOREIGN KEY (`GameId`) REFERENCES `Games` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TimelineEntries_LibraryEvents_LibraryEventId` FOREIGN KEY (`LibraryEventId`) REFERENCES `LibraryEvents` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_TimelineEntries_Reviews_ReviewId` FOREIGN KEY (`ReviewId`) REFERENCES `Reviews` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_TimelineEntries_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_TimelineEntries_FeedEligible_UserId_RecordedAt_Id` ON `TimelineEntries` (`FeedEligible`, `UserId`, `RecordedAt`, `Id`);

CREATE INDEX `IX_TimelineEntries_GameId` ON `TimelineEntries` (`GameId`);

CREATE UNIQUE INDEX `IX_TimelineEntries_LibraryEventId` ON `TimelineEntries` (`LibraryEventId`);

CREATE UNIQUE INDEX `IX_TimelineEntries_ReviewId` ON `TimelineEntries` (`ReviewId`);

CREATE INDEX `IX_TimelineEntries_UserId_OccurredOn_RecordedAt_Id` ON `TimelineEntries` (`UserId`, `OccurredOn`, `RecordedAt`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261115031802_AddTimelineEntries', '10.0.15');

COMMIT;
