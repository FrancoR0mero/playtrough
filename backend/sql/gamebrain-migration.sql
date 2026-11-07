START TRANSACTION;
ALTER TABLE `Games` ADD `GameBrainId` int NULL;

ALTER TABLE `Games` ADD `IgdbId` int NULL;

ALTER TABLE `Games` ADD `LegacyMetadataJson` json NULL;

ALTER TABLE `Games` ADD `MetadataExpiresAt` datetime(6) NULL;

ALTER TABLE `Games` ADD `SourceUrl` varchar(2048) NULL;

ALTER TABLE `Games` ADD `SteamId` int NULL;

CREATE UNIQUE INDEX `IX_Games_GameBrainId` ON `Games` (`GameBrainId`);

CREATE INDEX `IX_Games_MetadataExpiresAt` ON `Games` (`MetadataExpiresAt`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261104191259_AddGameBrainIdentity', '10.0.14');

ALTER TABLE `Games` ADD `ProviderDetailsJson` json NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261104191806_AddGameBrainDetailsCache', '10.0.14');

COMMIT;

