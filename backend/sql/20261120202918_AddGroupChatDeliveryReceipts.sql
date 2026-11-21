START TRANSACTION;
CREATE TABLE `GroupMessageReceipts` (
    `MessageId` bigint NOT NULL,
    `RecipientId` int NOT NULL,
    `ReceivedAt` datetime(6) NULL,
    PRIMARY KEY (`MessageId`, `RecipientId`),
    CONSTRAINT `FK_GroupMessageReceipts_GroupMessages_MessageId` FOREIGN KEY (`MessageId`) REFERENCES `GroupMessages` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GroupMessageReceipts_Users_RecipientId` FOREIGN KEY (`RecipientId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_GroupMessageReceipts_RecipientId` ON `GroupMessageReceipts` (`RecipientId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261120202918_AddGroupChatDeliveryReceipts', '10.0.15');

COMMIT;
