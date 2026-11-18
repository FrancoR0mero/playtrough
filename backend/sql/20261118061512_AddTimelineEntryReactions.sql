START TRANSACTION;
ALTER TABLE `TimelineEntries` ADD `CommentCount` int NOT NULL DEFAULT 0;

ALTER TABLE `TimelineEntries` ADD `DislikeCount` int NOT NULL DEFAULT 0;

ALTER TABLE `TimelineEntries` ADD `LikeCount` int NOT NULL DEFAULT 0;

CREATE TABLE `TimelineEntryReactions` (
    `EntryId` bigint NOT NULL,
    `UserId` int NOT NULL,
    `Value` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`EntryId`, `UserId`),
    CONSTRAINT `CK_TimelineEntryReactions_Value` CHECK (`Value` IN (0, 1)),
    CONSTRAINT `FK_TimelineEntryReactions_TimelineEntries_EntryId` FOREIGN KEY (`EntryId`) REFERENCES `TimelineEntries` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_TimelineEntryReactions_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

ALTER TABLE `TimelineEntries` ADD CONSTRAINT `CK_TimelineEntries_InteractionCounts` CHECK (LikeCount >= 0 AND DislikeCount >= 0 AND CommentCount >= 0);

CREATE INDEX `IX_TimelineEntryReactions_EntryId_Value_CreatedAt` ON `TimelineEntryReactions` (`EntryId`, `Value`, `CreatedAt`);

CREATE INDEX `IX_TimelineEntryReactions_UserId` ON `TimelineEntryReactions` (`UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118061512_AddTimelineEntryReactions', '10.0.15');

COMMIT;
