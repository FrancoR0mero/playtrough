START TRANSACTION;
CREATE TABLE `TimelineEntryComments` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `EntryId` bigint NOT NULL,
    `AuthorId` int NOT NULL,
    `Text` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `EditedAt` datetime(6) NULL,
    `LikeCount` int NOT NULL DEFAULT 0,
    `DislikeCount` int NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_TimelineEntryComments_InteractionCounts` CHECK (LikeCount >= 0 AND DislikeCount >= 0),
    CONSTRAINT `FK_TimelineEntryComments_TimelineEntries_EntryId` FOREIGN KEY (`EntryId`) REFERENCES `TimelineEntries` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_TimelineEntryComments_Users_AuthorId` FOREIGN KEY (`AuthorId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_TimelineEntryComments_AuthorId` ON `TimelineEntryComments` (`AuthorId`);

CREATE INDEX `IX_TimelineEntryComments_EntryId_CreatedAt_Id` ON `TimelineEntryComments` (`EntryId`, `CreatedAt`, `Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118063402_AddTimelineEntryComments', '10.0.15');

COMMIT;
