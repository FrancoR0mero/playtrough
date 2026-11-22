START TRANSACTION;
ALTER TABLE `SocialGroups` ADD `GenreId` varchar(40) NULL;

CREATE TABLE `GroupGenres` (
    `Id` varchar(40) NOT NULL,
    `Name` varchar(80) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `GroupTags` (
    `Id` varchar(40) NOT NULL,
    `Name` varchar(80) NOT NULL,
    `Category` varchar(20) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `GroupSubgenres` (
    `Id` varchar(40) NOT NULL,
    `GenreId` varchar(40) NOT NULL,
    `Name` varchar(80) NOT NULL,
    `Alias` varchar(40) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GroupSubgenres_GroupGenres_GenreId` FOREIGN KEY (`GenreId`) REFERENCES `GroupGenres` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `GroupTagAssignments` (
    `GroupId` int NOT NULL,
    `TagId` varchar(40) NOT NULL,
    PRIMARY KEY (`GroupId`, `TagId`),
    CONSTRAINT `FK_GroupTagAssignments_GroupTags_TagId` FOREIGN KEY (`TagId`) REFERENCES `GroupTags` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_GroupTagAssignments_SocialGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `SocialGroups` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `GroupSubgenreAssignments` (
    `GroupId` int NOT NULL,
    `SubgenreId` varchar(40) NOT NULL,
    PRIMARY KEY (`GroupId`, `SubgenreId`),
    CONSTRAINT `FK_GroupSubgenreAssignments_GroupSubgenres_SubgenreId` FOREIGN KEY (`SubgenreId`) REFERENCES `GroupSubgenres` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_GroupSubgenreAssignments_SocialGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `SocialGroups` (`Id`) ON DELETE CASCADE
);

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('accion', 'Acción');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('aventura', 'Aventura');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('carreras-conduccion', 'Carreras y conducción');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('cartas-juegos-mesa', 'Cartas y juegos de mesa');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('deportes', 'Deportes');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('disparos', 'Disparos');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('educacion-creatividad', 'Educación y creatividad');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('estrategia', 'Estrategia');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('gestion-construccion', 'Gestión y construcción');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('lucha', 'Lucha');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('narrativa-interactiva', 'Visual novels y narrativa interactiva');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('otros', 'Otros');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('party-games', 'Party games');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('plataformas', 'Plataformas');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('puzles-logica', 'Puzles y lógica');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('ritmo-musica', 'Ritmo y música');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('roguelike-roguelite', 'Roguelike y roguelite');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('rpg', 'RPG');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('sandbox-mundo-abierto', 'Sandbox y mundo abierto');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('simulacion', 'Simulación');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('supervivencia', 'Supervivencia');
SELECT ROW_COUNT();

INSERT INTO `GroupGenres` (`Id`, `Name`)
VALUES ('terror', 'Terror');
SELECT ROW_COUNT();


INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('android', 'plataforma', 'Android');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('casual', 'estilo', 'Casual');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('clasico', 'estilo', 'Clásico');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('coleccionismo', 'estilo', 'Coleccionismo');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('competitivo', 'modalidad', 'Competitivo');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('completionist', 'estilo', 'Completionist');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('cooperativo', 'modalidad', 'Cooperativo');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('hardcore', 'estilo', 'Hardcore');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('indie', 'estilo', 'Indie');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('ios', 'plataforma', 'iOS');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('local', 'modalidad', 'Local');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('multijugador', 'modalidad', 'Multijugador');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('nintendo', 'plataforma', 'Nintendo');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('online', 'modalidad', 'Online');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('pc', 'plataforma', 'PC');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('playstation', 'plataforma', 'PlayStation');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('pve', 'modalidad', 'PvE');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('pvp', 'modalidad', 'PvP');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('pvpve', 'modalidad', 'PvPvE');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('retro', 'estilo', 'Retro');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('retro-gaming', 'plataforma', 'Retro gaming');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('speedrunning', 'estilo', 'Speedrunning');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('un-jugador', 'modalidad', 'Un jugador');
SELECT ROW_COUNT();

INSERT INTO `GroupTags` (`Id`, `Category`, `Name`)
VALUES ('xbox', 'plataforma', 'Xbox');
SELECT ROW_COUNT();


INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('4x', NULL, 'estrategia', '4X');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('ajedrez', NULL, 'cartas-juegos-mesa', 'Ajedrez');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('aprendizaje-idiomas', NULL, 'educacion-creatividad', 'Aprendizaje de idiomas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('arcade-racing', NULL, 'carreras-conduccion', 'Arcade racing');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('arena-shooter', NULL, 'disparos', 'Arena shooter');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('auto-battler', NULL, 'estrategia', 'Auto battler');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('automatizacion-fabricas', NULL, 'gestion-construccion', 'Automatización de fábricas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('aventura-exploracion', NULL, 'aventura', 'Aventura de exploración');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('aventura-grafica', NULL, 'aventura', 'Aventura gráfica');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('aventura-textual', NULL, 'aventura', 'Aventura textual');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('baile-ritmico', NULL, 'ritmo-musica', 'Baile rítmico');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('baloncesto', NULL, 'deportes', 'Baloncesto');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('beat-em-up', NULL, 'accion', 'Beat ''em up');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('boomer-shooter', NULL, 'disparos', 'Boomer shooter');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('bullet-hell', NULL, 'disparos', 'Bullet hell');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('carreras-futuristas', NULL, 'carreras-conduccion', 'Carreras futuristas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('cartas-coleccionables', NULL, 'cartas-juegos-mesa', 'Cartas coleccionables');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('character-action', NULL, 'accion', 'Character action');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('cinematic-platformer', NULL, 'plataformas', 'Cinematic platformer');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('city-builder', NULL, 'gestion-construccion', 'City builder');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('collectathon', NULL, 'plataformas', 'Collectathon');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('construccion-libre', NULL, 'sandbox-mundo-abierto', 'Construcción libre');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('creacion-artistica', NULL, 'educacion-creatividad', 'Creación artística');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('creacion-niveles', NULL, 'educacion-creatividad', 'Creación de niveles');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('crpg', NULL, 'rpg', 'CRPG');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('deckbuilding', NULL, 'cartas-juegos-mesa', 'Deckbuilding');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('deduccion-social', NULL, 'party-games', 'Deducción social');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('deportes-extremos', NULL, 'deportes', 'Deportes extremos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('dibujo-adivinanzas', NULL, 'party-games', 'Dibujo y adivinanzas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('dungeon-crawler', NULL, 'rpg', 'Dungeon crawler');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('endless-runner', NULL, 'plataformas', 'Endless runner');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('escape-room', NULL, 'puzles-logica', 'Escape room');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('estrategia-tactica', NULL, 'estrategia', 'Estrategia táctica');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('experimentales', NULL, 'otros', 'Experimentales');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('exploracion-mundo-abierto', NULL, 'sandbox-mundo-abierto', 'Exploración en mundo abierto');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('extraction-shooter', NULL, 'disparos', 'Extraction shooter');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('fps', NULL, 'disparos', 'FPS');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('futbol', NULL, 'deportes', 'Fútbol');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('gestion-colonias', NULL, 'gestion-construccion', 'Gestión de colonias');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('gestion-deportiva', NULL, 'deportes', 'Gestión deportiva');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('gestion-recursos', NULL, 'gestion-construccion', 'Gestión de recursos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('golf', NULL, 'deportes', 'Golf');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('gran-estrategia', NULL, 'estrategia', 'Gran estrategia');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('hack-and-slash', NULL, 'accion', 'Hack and slash');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('hero-shooter', NULL, 'disparos', 'Hero shooter');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('hibridos', NULL, 'otros', 'Híbridos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('horror-accion', NULL, 'terror', 'Horror de acción');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('horror-cooperativo', NULL, 'terror', 'Horror cooperativo');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('horror-narrativo', NULL, 'terror', 'Horror narrativo');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('horror-psicologico', NULL, 'terror', 'Horror psicológico');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('instrumentos-virtuales', NULL, 'ritmo-musica', 'Instrumentos virtuales');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('jrpg', NULL, 'rpg', 'JRPG');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('juegos-de-dados', NULL, 'cartas-juegos-mesa', 'Juegos de dados');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('juegos-matematicos', NULL, 'educacion-creatividad', 'Juegos matemáticos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('juegos-mesa-digitales', NULL, 'cartas-juegos-mesa', 'Juegos de mesa digitales');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('karaoke', NULL, 'ritmo-musica', 'Karaoke');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('kart-racing', NULL, 'carreras-conduccion', 'Kart racing');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('looter-shooter', NULL, 'disparos', 'Looter shooter');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('lucha-2d', NULL, 'lucha', 'Lucha 2D');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('lucha-3d', NULL, 'lucha', 'Lucha 3D');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('lucha-arena', NULL, 'lucha', 'Lucha de arena');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('lucha-equipos', NULL, 'lucha', 'Lucha por equipos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('lucha-plataformas', NULL, 'lucha', 'Lucha de plataformas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('match-3', NULL, 'puzles-logica', 'Match-3');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('metroidvania', NULL, 'plataformas', 'Metroidvania');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('minijuegos', NULL, 'party-games', 'Minijuegos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('mmorpg', NULL, 'rpg', 'MMORPG');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('moba', NULL, 'estrategia', 'MOBA');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('monster-collecting', NULL, 'rpg', 'Monster collecting');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('mundo-abierto-procedural', NULL, 'sandbox-mundo-abierto', 'Mundo abierto procedural');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('musou', NULL, 'accion', 'Musou');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('narrativa-elecciones', NULL, 'narrativa-interactiva', 'Narrativa de elecciones');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('novela-visual-cinetica', NULL, 'narrativa-interactiva', 'Novela visual cinética');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('novela-visual-ramificada', NULL, 'narrativa-interactiva', 'Novela visual ramificada');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('objetos-ocultos', NULL, 'puzles-logica', 'Objetos ocultos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('parkour', NULL, 'accion', 'Parkour');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('point-and-click', NULL, 'aventura', 'Point and click');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('precision-platformer', NULL, 'plataformas', 'Precision platformer');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('programacion-educativa', NULL, 'educacion-creatividad', 'Programación educativa');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('puzles-espaciales', NULL, 'puzles-logica', 'Puzles espaciales');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('puzles-fisica', NULL, 'puzles-logica', 'Puzles de física');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('puzzle-platformer', NULL, 'plataformas', 'Puzzle platformer');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('rally', NULL, 'carreras-conduccion', 'Rally');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('ritmo-por-notas', NULL, 'ritmo-musica', 'Ritmo por notas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('roguelike-tradicional', NULL, 'roguelike-roguelite', 'Roguelike tradicional');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('roguelite-accion', NULL, 'roguelike-roguelite', 'Roguelite de acción');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('roguelite-cartas', NULL, 'roguelike-roguelite', 'Roguelite de cartas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('roguelite-tactico', NULL, 'roguelike-roguelite', 'Roguelite táctico');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('rpg-de-accion', 'ARPG', 'rpg', 'RPG de acción');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('rpg-narrativo', NULL, 'rpg', 'RPG narrativo');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('rpg-por-turnos', NULL, 'rpg', 'RPG por turnos');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('rts', NULL, 'estrategia', 'RTS');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('run-and-gun', NULL, 'accion', 'Run and gun');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('sandbox-fisico', NULL, 'sandbox-mundo-abierto', 'Sandbox físico');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('sigilo', NULL, 'accion', 'Sigilo');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('sim-racing', NULL, 'carreras-conduccion', 'Sim racing');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-citas', NULL, 'narrativa-interactiva', 'Simulador de citas');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-conduccion', NULL, 'simulacion', 'Simulador de conducción');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-construccion', NULL, 'simulacion', 'Simulador de construcción');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-granja', NULL, 'simulacion', 'Simulador de granja');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-negocios', NULL, 'simulacion', 'Simulador de negocios');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-vida', NULL, 'simulacion', 'Simulador de vida');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('simulador-vuelo', NULL, 'simulacion', 'Simulador de vuelo');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('sin-subgenero-especifico', NULL, 'otros', 'Sin subgénero específico');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('sokoban', NULL, 'puzles-logica', 'Sokoban');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('soulslike', NULL, 'rpg', 'Soulslike');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('street-racing', NULL, 'carreras-conduccion', 'Street racing');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('supervivencia-cooperativa', NULL, 'supervivencia', 'Supervivencia cooperativa');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('supervivencia-espacial', NULL, 'supervivencia', 'Supervivencia espacial');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('supervivencia-mundo-abierto', NULL, 'supervivencia', 'Supervivencia en mundo abierto');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('supervivencia-zombi', NULL, 'supervivencia', 'Supervivencia zombi');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('survival-crafting', NULL, 'supervivencia', 'Survival crafting');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('survival-horror', NULL, 'terror', 'Survival horror');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('tactical-rpg', NULL, 'rpg', 'Tactical RPG');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('tactical-shooter', NULL, 'disparos', 'Tactical shooter');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('tbs', NULL, 'estrategia', 'TBS');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('tenis', NULL, 'deportes', 'Tenis');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('tower-defense', NULL, 'estrategia', 'Tower defense');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('tps', NULL, 'disparos', 'TPS');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('trivia', NULL, 'party-games', 'Trivia');
SELECT ROW_COUNT();

INSERT INTO `GroupSubgenres` (`Id`, `Alias`, `GenreId`, `Name`)
VALUES ('wrpg', NULL, 'rpg', 'WRPG');
SELECT ROW_COUNT();


CREATE INDEX `IX_SocialGroups_DeletedAt_GenreId_Id` ON `SocialGroups` (`DeletedAt`, `GenreId`, `Id`);

CREATE INDEX `IX_SocialGroups_GenreId` ON `SocialGroups` (`GenreId`);

CREATE UNIQUE INDEX `IX_GroupGenres_Name` ON `GroupGenres` (`Name`);

CREATE INDEX `IX_GroupSubgenreAssignments_SubgenreId_GroupId` ON `GroupSubgenreAssignments` (`SubgenreId`, `GroupId`);

CREATE INDEX `IX_GroupSubgenres_GenreId` ON `GroupSubgenres` (`GenreId`);

CREATE UNIQUE INDEX `IX_GroupSubgenres_Name` ON `GroupSubgenres` (`Name`);

CREATE INDEX `IX_GroupTagAssignments_TagId_GroupId` ON `GroupTagAssignments` (`TagId`, `GroupId`);

CREATE UNIQUE INDEX `IX_GroupTags_Name` ON `GroupTags` (`Name`);

ALTER TABLE `SocialGroups` ADD CONSTRAINT `FK_SocialGroups_GroupGenres_GenreId` FOREIGN KEY (`GenreId`) REFERENCES `GroupGenres` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261122023203_AddGroupClassification', '10.0.15');

COMMIT;
