START TRANSACTION;
ALTER TABLE `Users` ADD `AvatarFileName` varchar(37) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261113060115_AddUserAvatar', '10.0.15');

COMMIT;
