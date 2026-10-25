START TRANSACTION;
CREATE TABLE `Games` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Provider` varchar(32) NOT NULL,
    `ExternalId` int NOT NULL,
    `Name` varchar(300) NOT NULL,
    `Slug` varchar(300) NOT NULL,
    `Summary` text NOT NULL,
    `CoverImageUrl` varchar(2048) NULL,
    `FirstReleaseDate` date NULL,
    `Platforms` json NOT NULL,
    `Genres` json NOT NULL,
    `Developers` json NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE UNIQUE INDEX `IX_Games_Provider_ExternalId` ON `Games` (`Provider`, `ExternalId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261025065759_AddGames', '10.0.13');

COMMIT;

