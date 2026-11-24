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

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261122023204_BackfillGroupClassification', '10.0.15');

COMMIT;
