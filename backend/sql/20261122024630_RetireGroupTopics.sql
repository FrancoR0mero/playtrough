START TRANSACTION;
INSERT INTO `GroupTagAssignments` (`GroupId`, `TagId`)
SELECT g.`Id`, 'cooperativo'
FROM `SocialGroups` g
WHERE g.`GenreId` IS NULL AND LOWER(TRIM(g.`Topic`)) = 'cooperativo'
  AND NOT EXISTS (
      SELECT 1 FROM `GroupTagAssignments` a WHERE a.`GroupId` = g.`Id` AND a.`TagId` = 'cooperativo'
  );

INSERT INTO `GroupTagAssignments` (`GroupId`, `TagId`)
SELECT g.`Id`, 'competitivo'
FROM `SocialGroups` g
WHERE g.`GenreId` IS NULL AND LOWER(TRIM(g.`Topic`)) = 'competitivo'
  AND NOT EXISTS (
      SELECT 1 FROM `GroupTagAssignments` a WHERE a.`GroupId` = g.`Id` AND a.`TagId` = 'competitivo'
  );

INSERT INTO `GroupTagAssignments` (`GroupId`, `TagId`)
SELECT g.`Id`, 'indie'
FROM `SocialGroups` g
WHERE g.`GenreId` IS NULL AND LOWER(TRIM(g.`Topic`)) = 'indie'
  AND NOT EXISTS (
      SELECT 1 FROM `GroupTagAssignments` a WHERE a.`GroupId` = g.`Id` AND a.`TagId` = 'indie'
  );

INSERT INTO `GroupTagAssignments` (`GroupId`, `TagId`)
SELECT g.`Id`, 'retro'
FROM `SocialGroups` g
WHERE g.`GenreId` IS NULL AND LOWER(TRIM(g.`Topic`)) = 'retro'
  AND NOT EXISTS (
      SELECT 1 FROM `GroupTagAssignments` a WHERE a.`GroupId` = g.`Id` AND a.`TagId` = 'retro'
  );

UPDATE `SocialGroups` SET `GenreId` = 'otros' WHERE `GenreId` IS NULL;

ALTER TABLE `SocialGroups` ADD `LegacyTopic` varchar(50) CHARACTER SET utf8mb4 NULL;

UPDATE `SocialGroups`
SET `LegacyTopic` = `Topic`
WHERE LOWER(TRIM(`Topic`)) NOT IN ('cooperativo', 'competitivo', 'indie', 'retro', 'general');

DROP INDEX IX_SocialGroups_DeletedAt_Topic_Id ON SocialGroups;

ALTER TABLE `SocialGroups` DROP COLUMN `Topic`;

ALTER TABLE `SocialGroups` DROP CONSTRAINT `FK_SocialGroups_GroupGenres_GenreId`;

ALTER TABLE `SocialGroups` MODIFY `GenreId` varchar(40) NOT NULL;

ALTER TABLE `SocialGroups` ADD CONSTRAINT `FK_SocialGroups_GroupGenres_GenreId` FOREIGN KEY (`GenreId`) REFERENCES `GroupGenres` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261122024630_RetireGroupTopics', '10.0.15');

COMMIT;
