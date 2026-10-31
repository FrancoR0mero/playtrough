START TRANSACTION;
ALTER TABLE `Games` ADD `RatingCount` int NOT NULL DEFAULT 0;

ALTER TABLE `Games` ADD `RatingSum` decimal(12,1) NOT NULL DEFAULT 0.0;

CREATE TABLE `Reviews` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `GameId` int NOT NULL,
    `Rating` decimal(2,1) NOT NULL,
    `Text` varchar(2000) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `EditedAt` datetime(6) NULL,
    `ConcurrencyToken` char(36) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Reviews_Games_GameId` FOREIGN KEY (`GameId`) REFERENCES `Games` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_Reviews_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) CHARACTER SET utf8mb4;

CREATE INDEX `IX_Reviews_GameId_CreatedAt_Id` ON `Reviews` (`GameId`, `CreatedAt`, `Id`);

CREATE INDEX `IX_Reviews_GameId_Rating` ON `Reviews` (`GameId`, `Rating`);

CREATE UNIQUE INDEX `IX_Reviews_UserId_GameId` ON `Reviews` (`UserId`, `GameId`);

CREATE TRIGGER `TR_Users_DeleteReviewRatings` BEFORE DELETE ON `Users`
FOR EACH ROW
UPDATE `Games` AS g
INNER JOIN (
    SELECT `GameId`, COUNT(*) AS `ReviewCount`, SUM(`Rating`) AS `ReviewSum`
    FROM `Reviews` WHERE `UserId` = OLD.`Id` GROUP BY `GameId`
) AS removed ON removed.`GameId` = g.`Id`
SET g.`RatingCount` = g.`RatingCount` - removed.`ReviewCount`,
    g.`RatingSum` = g.`RatingSum` - removed.`ReviewSum`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261031000047_AddReviews', '10.0.14');

COMMIT;

