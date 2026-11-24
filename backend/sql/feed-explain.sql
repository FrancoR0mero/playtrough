-- Read-only EXPLAIN of the actual EF query. Parameters below come from the local diagnostic.
SET @viewerId = 20;
SET @eligible = TRUE;

EXPLAIN FORMAT=JSON SELECT `t`.`Id`, `t`.`Type`, `t`.`OccurredOn`, `t`.`RecordedAt`, `g`.`Id`, COALESCE(`g`.`GameBrainId`, `g`.`ExternalId`), `g`.`Name`, `g`.`Slug`, `g`.`CoverImageUrl`, CASE
    WHEN `g`.`FirstReleaseDate` IS NOT NULL THEN EXTRACT(year FROM `g`.`FirstReleaseDate`)
END, `t`.`FromStatus` IS NOT NULL, `t`.`FromStatus`, `t`.`ToStatus` IS NOT NULL, `t`.`ToStatus`, (`t`.`Type` = 'reviewed') AND `r`.`Id` IS NOT NULL, `r`.`Id`, `r`.`Rating`, `r`.`Text`, `u0`.`UserName`, `u0`.`AvatarFileName`
FROM `TimelineEntries` AS `t`
INNER JOIN (
    SELECT `f`.`UserHighId`
    FROM `Friendships` AS `f`
    WHERE `f`.`UserLowId` = @viewerId
    UNION ALL
    SELECT `f0`.`UserLowId` AS `UserHighId`
    FROM `Friendships` AS `f0`
    WHERE `f0`.`UserHighId` = @viewerId
) AS `u` ON `t`.`UserId` = `u`.`UserHighId`
INNER JOIN `Users` AS `u0` ON `t`.`UserId` = `u0`.`Id`
INNER JOIN `Games` AS `g` ON `t`.`GameId` = `g`.`Id`
LEFT JOIN `Reviews` AS `r` ON `t`.`ReviewId` = `r`.`Id`
WHERE ((`t`.`FeedEligible` = @eligible) AND NOT (`t`.`IsHidden`)) AND (`u0`.`EmailConfirmed` AND NOT EXISTS (
    SELECT 1
    FROM `UserBlocks` AS `u1`
    WHERE ((`u1`.`BlockerId` = @viewerId) AND (`u1`.`BlockedId` = `u0`.`Id`)) OR ((`u1`.`BlockerId` = `u0`.`Id`) AND (`u1`.`BlockedId` = @viewerId))))
ORDER BY `t`.`RecordedAt` DESC, `t`.`Id` DESC
LIMIT 21;
