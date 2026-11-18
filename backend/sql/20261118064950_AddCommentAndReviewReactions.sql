START TRANSACTION;
ALTER TABLE `Reviews` ADD `DislikeCount` int NOT NULL DEFAULT 0;

ALTER TABLE `Reviews` ADD `LikeCount` int NOT NULL DEFAULT 0;

CREATE TABLE `ReviewReactions` (
    `ReviewId` int NOT NULL,
    `UserId` int NOT NULL,
    `Value` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`ReviewId`, `UserId`),
    CONSTRAINT `CK_ReviewReactions_Value` CHECK (`Value` IN (0, 1)),
    CONSTRAINT `FK_ReviewReactions_Reviews_ReviewId` FOREIGN KEY (`ReviewId`) REFERENCES `Reviews` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ReviewReactions_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `TimelineEntryCommentReactions` (
    `CommentId` bigint NOT NULL,
    `UserId` int NOT NULL,
    `Value` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`CommentId`, `UserId`),
    CONSTRAINT `CK_TimelineEntryCommentReactions_Value` CHECK (`Value` IN (0, 1)),
    CONSTRAINT `FK_TimelineEntryCommentReactions_TimelineEntryComments_CommentId` FOREIGN KEY (`CommentId`) REFERENCES `TimelineEntryComments` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_TimelineEntryCommentReactions_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

ALTER TABLE `Reviews` ADD CONSTRAINT `CK_Reviews_InteractionCounts` CHECK (LikeCount >= 0 AND DislikeCount >= 0);

CREATE INDEX `IX_ReviewReactions_UserId` ON `ReviewReactions` (`UserId`);

CREATE INDEX `IX_TimelineEntryCommentReactions_UserId` ON `TimelineEntryCommentReactions` (`UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118064950_AddCommentAndReviewReactions', '10.0.15');

COMMIT;
