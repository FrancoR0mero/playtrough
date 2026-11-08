START TRANSACTION;
CREATE TABLE `FriendRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SenderId` int NOT NULL,
    `ReceiverId` int NOT NULL,
    `Status` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `RespondedAt` datetime(6) NULL,
    `PendingPairKey` bigint AS (CASE WHEN `Status` = 0 THEN CASE WHEN `SenderId` < `ReceiverId` THEN `SenderId` * 4294967296 + `ReceiverId` ELSE `ReceiverId` * 4294967296 + `SenderId` END ELSE NULL END) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_FriendRequests_DifferentUsers` CHECK (`SenderId` <> `ReceiverId`),
    CONSTRAINT `FK_FriendRequests_Users_ReceiverId` FOREIGN KEY (`ReceiverId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_FriendRequests_Users_SenderId` FOREIGN KEY (`SenderId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `Friendships` (
    `UserLowId` int NOT NULL,
    `UserHighId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`UserLowId`, `UserHighId`),
    CONSTRAINT `CK_Friendships_CanonicalPair` CHECK (`UserLowId` < `UserHighId`),
    CONSTRAINT `FK_Friendships_Users_UserHighId` FOREIGN KEY (`UserHighId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_Friendships_Users_UserLowId` FOREIGN KEY (`UserLowId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_FriendRequests_PendingPairKey` ON `FriendRequests` (`PendingPairKey`);

CREATE INDEX `IX_FriendRequests_ReceiverId_Status_CreatedAt_Id` ON `FriendRequests` (`ReceiverId`, `Status`, `CreatedAt`, `Id`);

CREATE INDEX `IX_FriendRequests_SenderId_Status_CreatedAt_Id` ON `FriendRequests` (`SenderId`, `Status`, `CreatedAt`, `Id`);

CREATE INDEX `IX_Friendships_UserHighId` ON `Friendships` (`UserHighId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261108000454_AddFriendships', '10.0.14');

COMMIT;

